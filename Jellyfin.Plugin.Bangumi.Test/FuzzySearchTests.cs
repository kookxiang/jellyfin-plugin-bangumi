using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class FuzzySearchTests
{
    [DataTestMethod]
    [DataRow(0)]
    [DataRow(30)]
    public async Task LaterAliasMatchIsReturnedAfterEarlierBatchMatches(int minScore)
    {
        var subjects = Enumerable.Range(1, 6)
            .Select(id => new Subject
            {
                Id = id,
                OriginalNameRaw = id == 6 ? "unrelated" : $"target version {id}"
            })
            .ToList();
        var requestedIds = new List<int>();

        Task<Subject?> GetDetails(int id, CancellationToken _)
        {
            requestedIds.Add(id);
            if (id == 5)
                return Task.FromResult<Subject?>(null);
            return Task.FromResult<Subject?>(id == 6 ? WithTargetAlias(subjects[5]) : subjects[id - 1]);
        }

        var results = await BangumiApi.RankSubjectsByFuzzScore(subjects, "target", minScore,
            GetDetails, CancellationToken.None);

        Assert.AreEqual(6, requestedIds.Count, "all candidate batches should be checked");
        Assert.AreEqual(6, results.Count, "a missing detail response should retain the search result");
        Assert.AreEqual(6, results[0].Id, "an exact alias in a later batch should rank first");
    }

    [TestMethod]
    public async Task ExactTitleDoesNotSkipLaterAliasMatches()
    {
        var subjects = Enumerable.Range(1, 6)
            .Select(id => new Subject
            {
                Id = id,
                OriginalNameRaw = id == 1 ? "target" : $"unrelated {id}"
            })
            .ToList();

        Task<Subject?> GetDetails(int id, CancellationToken _) =>
            Task.FromResult<Subject?>(id == 6 ? WithTargetAlias(subjects[5]) : subjects[id - 1]);

        var results = await BangumiApi.RankSubjectsByFuzzScore(subjects, "target", 90,
            GetDetails, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { 1, 6 }, results.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    public async Task SingleCandidateRespectsThreshold()
    {
        var unrelated = new Subject { Id = 1, OriginalNameRaw = "unrelated" };
        var exact = new Subject { Id = 2, OriginalNameRaw = "target" };

        Task<Subject?> GetDetails(int id, CancellationToken _) =>
            Task.FromResult<Subject?>(id == 1 ? unrelated : exact);

        var rejected = await BangumiApi.RankSubjectsByFuzzScore([unrelated], "target", 90,
            GetDetails, CancellationToken.None);
        var accepted = await BangumiApi.RankSubjectsByFuzzScore([exact], "target", 100,
            GetDetails, CancellationToken.None);

        Assert.AreEqual(0, rejected.Count);
        Assert.AreEqual(2, accepted.Single().Id, "a score equal to the threshold should be retained");
    }

    private static Subject WithTargetAlias(Subject subject) => new()
    {
        Id = subject.Id,
        OriginalNameRaw = subject.OriginalNameRaw,
        InfoBox = new InfoBox { ["别名"] = "target" }
    };
}
