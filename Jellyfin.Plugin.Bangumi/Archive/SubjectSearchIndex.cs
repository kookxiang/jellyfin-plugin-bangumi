using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Cjk;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;
using IoDirectory = System.IO.Directory;
using ThreadLock = System.Threading.Lock;

namespace Jellyfin.Plugin.Bangumi.Archive;

/// <summary>
/// 基于 Lucene.Net 的条目名称索引，按 <see cref="SubjectType"/> 分目录存储。
/// </summary>
public class SubjectSearchIndex(ArchiveData archive)
{
    private const LuceneVersion LuceneVersion = LuceneVersion.LUCENE_48;
    private const string IdField = "id";
    private const string NameField = "name";

    /// <summary>
    /// SubjectType 对应的索引目录名
    /// </summary>
    private static readonly Dictionary<SubjectType, string> _indexDirectoryNames = Enum.GetValues<SubjectType>()
        .Cast<SubjectType>()
        .Where(t => t != SubjectType.None)
        .ToDictionary(t => t, t => $"subject-search-{(int)t}.lucene");

    private static readonly Dictionary<SubjectType, CachedSearcher> _cache = [];
    private static readonly ThreadLock _cacheLock = new();

    private sealed class CachedSearcher(FSDirectory directory, DirectoryReader reader, IndexSearcher searcher) : IDisposable
    {
        public FSDirectory Directory { get; } = directory;
        public DirectoryReader Reader { get; } = reader;
        public IndexSearcher Searcher { get; } = searcher;

        public void Dispose()
        {
            Reader.Dispose();
            Directory.Dispose();
        }
    }

    /// <summary>
    /// 获取指定类型的索引目录路径。
    /// </summary>
    /// <param name="type">条目类型</param>
    /// <returns>对应索引目录的完整路径</returns>
    private string GetDirectoryPath(SubjectType type) =>
        Path.Combine(archive.BasePath, _indexDirectoryNames.GetOrDefault(type) ?? string.Empty);

    /// <summary>
    /// 检查指定类型的索引目录是否存在且包含有效索引。
    /// </summary>
    /// <param name="type">条目类型</param>
    /// <returns>索引是否存在</returns>
    public bool Exists(SubjectType type)
    {
        var path = GetDirectoryPath(type);
        if (!IoDirectory.Exists(path)) return false;

        using var directory = FSDirectory.Open(path);
        return DirectoryReader.IndexExists(directory);
    }

    /// <summary>
    /// 检查是否存在任意类型的索引。
    /// </summary>
    /// <returns>如果任一类型的索引存在则返回 true，否则返回 false</returns>
    public bool AnyExists() => Enum.GetValues<SubjectType>()
        .Any(t => t != SubjectType.None && Exists(t));

    #region 索引构建与缓存清理

    /// <summary>
    /// 遍历 subject.jsonlines，为每个 <see cref="SubjectType"/> 生成独立的 Lucene 索引目录。
    /// </summary>
    public Task GenerateIndex(CancellationToken token)
    {
        // 全量重建前先清理旧索引目录，避免残留数据干扰。
        ClearIndexDirectories();
        var writers = new Dictionary<SubjectType, IndexWriter>();

        try
        {
            // 遍历归档条目，按类型写入对应索引。
            foreach (var subject in archive.Subject.Enumerate())
            {
                token.ThrowIfCancellationRequested();
                if (subject.Type == SubjectType.None) continue;

                var writer = GetOrCreateWriter(subject.Type, writers);
                var document = BuildDocument(subject);
                writer.UpdateDocument(new Term(IdField, subject.Id.ToString()), document);
            }

            // 刷新并提交所有类型索引，确保磁盘上的索引可被检索器读取。
            foreach (var writer in writers.Values)
            {
                token.ThrowIfCancellationRequested();
                writer.Flush(triggerMerge: false, applyAllDeletes: true);
                writer.Commit();
            }
        }
        finally
        {
            // 无论是否异常都释放写入器，并清空检索缓存以加载最新索引。
            foreach (var writer in writers.Values)
                writer.Dispose();

            ClearCache();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 清理所有类型的旧索引目录。
    /// </summary>
    private void ClearIndexDirectories()
    {
        foreach (SubjectType type in Enum.GetValues<SubjectType>())
        {
            if (type == SubjectType.None) continue;

            var path = GetDirectoryPath(type);
            if (IoDirectory.Exists(path))
                IoDirectory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    /// 获取指定类型的索引写入器；若不存在则创建并缓存。
    /// </summary>
    /// <param name="type">目标 SubjectType</param>
    /// <param name="writers">当前构建过程中的写入器缓存</param>
    /// <returns>可用的 <see cref="IndexWriter"/></returns>
    private IndexWriter GetOrCreateWriter(SubjectType type, Dictionary<SubjectType, IndexWriter> writers)
    {
        if (writers.TryGetValue(type, out var existed))
            return existed;

        var path = GetDirectoryPath(type);
        IoDirectory.CreateDirectory(path);

        var directory = FSDirectory.Open(path);
        var config = new IndexWriterConfig(LuceneVersion, CreateAnalyzer())
        {
            OpenMode = OpenMode.CREATE,
            Similarity = new BM25Similarity()
        };

        var writer = new IndexWriter(directory, config);
        writers[type] = writer;
        return writer;
    }

    /// <summary>
    /// 创建用于中日韩文本分词的分析器实例。
    /// </summary>
    /// <returns><see cref="Analyzer"/> 实例</returns>
    private static Analyzer CreateAnalyzer()
    {
        return new CJKAnalyzer(LuceneVersion);
    }

    /// <summary>
    /// 将归档条目转换为 Lucene 文档，写入可检索名称字段。
    /// </summary>
    /// <param name="subject">归档条目</param>
    /// <returns>用于索引的 <see cref="Document"/></returns>
    private static Document BuildDocument(Data.Subject subject)
    {
        var document = new Document
        {
            new StringField(IdField, subject.Id.ToString(), Field.Store.YES)
        };

        if (!string.IsNullOrWhiteSpace(subject.OriginalName))
            document.Add(new TextField(NameField, subject.OriginalName, Field.Store.NO));
        if (!string.IsNullOrWhiteSpace(subject.ChineseName))
            document.Add(new TextField(NameField, subject.ChineseName, Field.Store.NO));

        foreach (var alias in subject.ToSubject().Alias ?? [])
        {
            if (!string.IsNullOrWhiteSpace(alias))
                document.Add(new TextField(NameField, alias, Field.Store.NO));
        }

        return document;
    }

    /// <summary>
    /// 释放并清空所有已缓存的检索器。
    /// </summary>
    private static void ClearCache()
    {
        lock (_cacheLock)
        {
            foreach (var cached in _cache.Values)
                cached.Dispose();
            _cache.Clear();
        }
    }

    #endregion

    #region 搜索

    /// <summary>
    /// 对指定关键词执行检索，返回按相关性排序的条目 ID 列表。
    /// 使用 BM25 评分，不要求命中全部分词，且会过滤完全不命中的结果。
    /// </summary>
    /// <param name="keyword">要搜索的关键词</param>
    /// <param name="type">可选的 SubjectType，若指定则仅在该类型中搜索</param>
    /// <param name="pageSize">返回结果条数上限</param>
    /// <returns>按匹配度降序排列的条目 ID 列表</returns>
    public IReadOnlyList<int> Search(string keyword, SubjectType? type, int pageSize)
    {
        // 基础参数校验。
        if (string.IsNullOrWhiteSpace(keyword) || pageSize <= 0)
            return [];

        // 将关键词分词并构建查询对象。
        var query = BuildQuery(keyword);
        if (query is null)
            return [];

        // 指定类型时仅搜索该类型索引。
        if (type != null)
            return SearchInType(type.Value, query, pageSize);

        // 未指定类型时聚合所有可用类型索引进行搜索。
        var cachedSearchers = new List<CachedSearcher>();
        foreach (SubjectType t in Enum.GetValues<SubjectType>())
        {
            if (t == SubjectType.None) continue;
            var cached = GetOrLoadSearcher(t);
            if (cached is null) continue;
            cachedSearchers.Add(cached);
        }

        if (cachedSearchers.Count == 0)
            return [];

        // 只有一个时直接复用缓存实例，避免不必要的 MultiReader 包装。
        if (cachedSearchers.Count == 1)
            return SearchAndExtractIds(cachedSearchers[0].Searcher, query, pageSize);

        // 多类型索引通过 MultiReader 合并后统一检索。
        using var multiReader = new MultiReader([.. cachedSearchers.Select(c => c.Reader)], closeSubReaders: false);
        var searcher = new IndexSearcher(multiReader)
        {
            Similarity = new BM25Similarity()
        };
        return SearchAndExtractIds(searcher, query, pageSize);
    }

    /// <summary>
    /// 将关键词分词后构建 Lucene 查询。
    /// </summary>
    /// <param name="keyword">用户输入关键词</param>
    /// <returns>查询对象；无有效分词时返回 null</returns>
    private static Query? BuildQuery(string keyword)
    {
        var terms = AnalyzeTerms(keyword);
        if (terms.Count == 0)
            return null;

        if (terms.Count == 1)
            return new TermQuery(new Term(NameField, terms[0]));

        var query = new BooleanQuery { MinimumNumberShouldMatch = 1 };
        foreach (var term in terms)
            query.Add(new TermQuery(new Term(NameField, term)), Occur.SHOULD);

        return query;
    }

    /// <summary>
    /// 使用索引分析器对关键词分词并去重。
    /// </summary>
    /// <param name="keyword">用户输入关键词</param>
    /// <returns>去重后的分词列表</returns>
    private static List<string> AnalyzeTerms(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return [];

        using var analyzer = CreateAnalyzer();
        using var tokenStream = analyzer.GetTokenStream(NameField, keyword);

        var termAttribute = tokenStream.AddAttribute<ICharTermAttribute>();
        tokenStream.Reset();

        var terms = new HashSet<string>(StringComparer.Ordinal);
        while (tokenStream.IncrementToken())
        {
            var term = termAttribute.ToString();
            if (!string.IsNullOrWhiteSpace(term))
                terms.Add(term);
        }

        tokenStream.End();
        return [.. terms];
    }

    /// <summary>
    /// 在指定类型索引中执行查询并返回按评分排序的 ID。
    /// </summary>
    /// <param name="type">目标 SubjectType</param>
    /// <param name="query">Lucene 查询对象</param>
    /// <param name="pageSize">返回结果条数上限</param>
    /// <returns>按匹配度降序排列的条目 ID 列表</returns>
    private IReadOnlyList<int> SearchInType(SubjectType type, Query query, int pageSize)
    {
        var cached = GetOrLoadSearcher(type);
        if (cached is null)
            return [];

        return SearchAndExtractIds(cached.Searcher, query, pageSize);
    }

    /// <summary>
    /// 从缓存中获取或加载指定类型的检索器。
    /// </summary>
    /// <param name="type">目标 SubjectType</param>
    /// <returns>缓存检索器，若索引不存在返回 null</returns>
    private CachedSearcher? GetOrLoadSearcher(SubjectType type)
    {
        // 优先从缓存读取，避免重复打开索引目录。
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(type, out var cached))
                return cached;
        }

        // 索引目录不存在或尚未建索引时直接返回。
        var path = GetDirectoryPath(type);
        if (!IoDirectory.Exists(path))
            return null;

        var directory = FSDirectory.Open(path);
        if (!DirectoryReader.IndexExists(directory))
        {
            directory.Dispose();
            return null;
        }

        // 加载目录与读者，构建检索器。
        var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader)
        {
            Similarity = new BM25Similarity()
        };
        var loaded = new CachedSearcher(directory, reader, searcher);

        // 写回缓存；若并发下已有实例，则复用已存在实例。
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(type, out var existed))
            {
                loaded.Dispose();
                return existed;
            }

            _cache[type] = loaded;
        }

        return loaded;
    }

    /// <summary>
    /// 执行 Lucene 查询并从文档中提取条目 ID。
    /// </summary>
    /// <param name="searcher">索引检索器</param>
    /// <param name="query">Lucene 查询对象</param>
    /// <param name="pageSize">返回结果条数上限</param>
    /// <returns>按匹配度降序排列的条目 ID 列表</returns>
    private static IReadOnlyList<int> SearchAndExtractIds(IndexSearcher searcher, Query query, int pageSize)
    {
        var topDocs = searcher.Search(query, pageSize);
        if (topDocs.ScoreDocs.Length == 0)
            return [];

        var ids = new List<int>(topDocs.ScoreDocs.Length);
        foreach (var scoreDoc in topDocs.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var idText = doc.Get(IdField);
            if (int.TryParse(idText, out var id))
                ids.Add(id);
        }

        return ids.AsReadOnly();
    }

    #endregion
}

