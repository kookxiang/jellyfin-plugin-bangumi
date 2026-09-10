import 'material-design-icons-iconfont/dist/material-design-icons.css';
await import(new URLSearchParams(location.search).has('production') ? '../dist/bangumi.js' : '../src/main.ts');
const config = {
    BaseServerUrl: 'https://api.bgm.tv',
    BaseWebsiteUrl: 'https://bgm.tv',
    ProxyServerUrl: '',
    RequestTimeout: 5000,
    SeasonGuessMaxSearchCount: 2,
    RatingUpdateMinInterval: 14,
    TranslationPreference: 'Chinese',
    PersonTranslationPreference: 'Original',
    EpisodeParser: 'Torrent',
    MergeEpisodeVersionsByBangumiId: false,
    DaysBeforeUsingArchiveData: 14,
    SkipNSFWPlaybackReport: true,
    PrivateNSFWPlaybackReport: false,
    ReportPlaybackStatusToBangumi: true,
    ReportManualStatusChangeToBangumi: false,
    MiscExcludeRegexFullPath: 'Extras',
    MiscExcludeRegexFolderName: 'Extras',
    MiscExcludeRegexFileName: 'NCOP',
    SpExcludeRegexFullPath: '',
    SpExcludeRegexFolderName: 'SP',
    SpExcludeRegexFileName: 'SP',
    ExcludeWhitelistRegexFullPath: '',
    ExcludeWhitelistRegexFolderName: '',
    ExcludeWhitelistRegexFileName: '',
    FutureSetting: { preserve: true },
};
let saved,
    loads = 0;
let confirmed = false;
const toolCalls = [];
let directoryType;
const report = document.querySelector('#report');
const services = {
    api: {
        getPluginConfiguration: async () => {
            loads++;
            return structuredClone(config);
        },
        updatePluginConfiguration: async (id, value) => {
            saved = value;
        },
        getUrl: (path) => path,
        getScheduledTasks: async () => [{ Id: 'archive-task', Key: 'ArchiveDataDownloadTask', Category: 'Bangumi' }],
        getCurrentUserId: () => 'test-user',
        getCurrentUser: async () => ({ Id: 'test-user', Name: '测试用户' }),
        getUsers: async () => [
            { Id: 'test-user', Name: '测试用户' },
            { Id: 'another', Name: '其他用户' },
        ],
        getJSON: async (url) => {
            const state = new URLSearchParams(location.search).get('auth');
            if (url.includes('OAuthState') && ['bound', 'expired'].includes(state))
                return {
                    nickname: '测试 Bangumi 账号',
                    effective: '2026-01-01T12:00:00',
                    expire: '2027-01-01T12:00:00',
                    autoRefresh: true,
                    expired: state === 'expired',
                };
            return url.includes('Archive')
                ? new URLSearchParams(location.search).get('archive') === 'ready'
                    ? {
                          size: 187432960,
                          path: '/var/lib/jellyfin/plugins/Bangumi/archive',
                          time: '2026-09-09T01:00:00+08:00',
                      }
                    : { size: 0 }
                : null;
        },
        fetch: async ({ url, data, type }) => {
            if (url.includes('/MediaLibrary/Configuration/') && type === 'PUT') {
                directoryType = JSON.parse(data).Type;
            }
            if (url.includes('/MediaLibrary/Preview/')) {
                toolCalls.push({ url, data });
                const query = new URL(url, location.origin).searchParams;
                const offset = Number(query.get('offset') || 0);
                return new Response(
                    JSON.stringify({
                        EpisodeId: query.get('episodeId') || 'sample-1',
                        FileName: 'Example - 27.mkv',
                        Parser: 'Torrent',
                        DetectedIndex: 27,
                        BangumiIndex: 27 - offset,
                        JellyfinIndex: query.get('correctIndex') === 'true' ? 27 - offset : 27,
                        Message: '模拟预览，不写入配置或元数据。',
                    }),
                );
            }
            if (/Tools\/(DuplicatedEpisodesDetector|FixEpisodeMetadata|MissingBangumiId)\//.test(url)) {
                toolCalls.push({ url, data });
                const result = url.endsWith('/Libraries')
                    ? [{ Id: 'name:Anime', Name: '动漫' }]
                    : url.endsWith('/Scan')
                      ? [
                            {
                                BangumiId: 1,
                                Title: '重复剧集',
                                Items: [
                                    {
                                        Id: 'file-1',
                                        Path: '/media/duplicate.mkv',
                                        LastModified: '2026-01-01',
                                        Ticks: 600000000,
                                    },
                                    {
                                        Id: 'file-2',
                                        Path: '/media/new.mkv',
                                        LastModified: '2026-02-01',
                                        Ticks: 600000000,
                                    },
                                    {
                                        Id: 'file-3',
                                        Path: '/media/tied.mkv',
                                        LastModified: '2026-02-01',
                                        Ticks: 600000000,
                                    },
                                ],
                            },
                        ]
                      : url.split('?')[0].endsWith('/Items')
                        ? [
                              {
                                  Id: 'missing-1',
                                  SeriesId: 'series-1',
                                  SeriesName: '测试系列',
                                  SeasonId: 'season-1',
                                  SeasonName: '第一季',
                                  Name: '缺失 ID 的剧集',
                                  Path: '/media/missing.mkv',
                                  BangumiProviderEnabled: true,
                              },
                              {
                                  Id: 'disabled-1',
                                  SeriesId: 'series-1',
                                  SeriesName: '测试系列',
                                  SeasonId: 'season-1',
                                  SeasonName: '第一季',
                                  Name: '未启用提供程序',
                                  BangumiProviderEnabled: false,
                              },
                          ]
                        : url.endsWith('/Refresh')
                          ? {
                                QueuedCount: 1,
                                QueuedItemIds: ['missing-1'],
                                FailedCount: 0,
                                SkippedCount: 0,
                                ProviderDisabledCount: 0,
                            }
                          : url.endsWith('/Run')
                            ? { TotalCount: 10, RemovedCount: 2, ValidCount: 7, NoIdCount: 1 }
                            : null;
                return new Response(JSON.stringify(result));
            }
            return new Response(
                JSON.stringify(
                    url.includes('/Configuration/')
                        ? {
                              ItemId: 'series',
                              ItemName: '测试番剧',
                              Exists: true,
                              DirectoryPath: '/media/anime',
                              Id: 1,
                              Type: directoryType,
                          }
                        : {
                              Items: [{ Id: 'series', Name: '测试番剧', Path: '/media/anime', Children: [] }],
                              Libraries: [],
                              TotalRecordCount: 1,
                              TotalItemCount: 1,
                          },
                ),
            );
        },
    },
    dashboard: {
        showLoadingMsg() {},
        hideLoadingMsg() {},
        processPluginConfigurationUpdateResult() {
            report.textContent = '模拟保存成功';
        },
        alert(message) {
            report.textContent = typeof message === 'string' ? message : message.message;
        },
        confirm(message, title, cb) {
            cb(confirmed);
        },
        navigate() {},
    },
};
const previewTheme = new URLSearchParams(location.search).get('theme');
if (['light', 'purple'].includes(previewTheme)) document.body.classList.add(previewTheme);
const app = document.createElement('jellyfin-plugin-bangumi');
app.services = services;
document.querySelector('#page').append(app);
document.querySelector('#theme').onclick = () => document.body.classList.toggle('light');
document.querySelector('#remount').onclick = () => {
    app.remove();
    document.querySelector('#page').append(app);
};
const tick = () => new Promise((resolve) => setTimeout(resolve, 50));
const assert = (value, message) => {
    if (!value) throw new Error(message);
};
document.querySelector('#run').onclick = async () => {
    try {
        const root = app.shadowRoot;
        const saveBar = root.querySelector('.submit-button-container');
        assert(saveBar.hidden, '初始无修改隐藏保存栏');
        const dirtyField = root.querySelector('#ReportPlaybackStatusToBangumi');
        dirtyField.click();
        assert(!saveBar.hidden, '修改后显示保存栏');
        dirtyField.click();
        assert(saveBar.hidden, '还原原值隐藏保存栏');
        root.querySelector('[data-target=archive]').click();
        assert(!root.querySelector('#config-archive-update-task').disabled, '有无数据库都能进入计划任务');
        assert(
            root.querySelector('#RefreshRatingWhenArchiveUpdate').closest('bangumi-checkbox-group'),
            '离线更新选项分组',
        );
        root.querySelector('[data-target=account]').click();
        const checkbox = root.querySelector('#ReportPlaybackStatusToBangumi');
        const checkboxLabel = root.querySelector('label[for=ReportPlaybackStatusToBangumi]');
        assert(checkbox.closest('bangumi-checkbox')?.shadowRoot, 'Checkbox 独立组件');
        const checked = checkbox.checked;
        checkboxLabel.click();
        assert(checkbox.checked !== checked, '点击整行标签切换');
        checkbox.disabled = true;
        checkboxLabel.click();
        assert(checkbox.checked !== checked, '禁用组件不切换');
        checkbox.disabled = false;
        checkboxLabel.click();
        assert(checkbox.checked === checked, '恢复勾选状态');
        const skipReport = root.querySelector('#SkipNSFWPlaybackReport');
        const privateReport = root.querySelector('#PrivateNSFWPlaybackReport');
        assert(privateReport.closest('bangumi-checkbox-group'), 'NSFW 选项分组');
        assert(privateReport.disabled && privateReport.getClientRects().length, '禁用上报时私密选项可见但禁用');
        skipReport.click();
        assert(!privateReport.disabled, '允许上报后私密选项可用');
        privateReport.checked = true;
        skipReport.click();
        root.querySelector('label[for=PrivateNSFWPlaybackReport]').click();
        assert(privateReport.disabled && privateReport.checked, '禁用保留原有私密设置且不可切换');
        privateReport.checked = config.PrivateNSFWPlaybackReport;
        root.querySelector('[data-target=network]').click();
        assert(root.querySelector('bangumi-navigation')?.shadowRoot, '独立导航组件');
        assert(root.querySelector('[data-target=network]').getAttribute('aria-current') === 'page', '导航选中状态');
        assert(root.querySelectorAll('bangumi-navigation [aria-current=page]').length === 1, '仅一个当前页面');
        assert(root.querySelector('.bangumi-settings-panel.active').dataset.section === 'network', '导航与面板同步');
        assert(getComputedStyle(root.querySelector('#BaseServerUrl')).borderRadius === '10px', 'Shadow DOM 样式隔离');
        const customSelect = root.querySelector('#RequestTimeout').closest('bangumi-select');
        const trigger = customSelect.shadowRoot.querySelector('button');
        trigger.click();
        assert(trigger.getAttribute('aria-expanded') === 'true', '自定义下拉展开');
        trigger.click();
        assert(trigger.getAttribute('aria-expanded') === 'false', '再次点击收起');
        trigger.click();
        assert(trigger.getAttribute('aria-expanded') === 'true', '收起后可立即重新展开');
        customSelect.shadowRoot.querySelector('[data-index="1"]').click();
        assert(
            root.querySelector('#RequestTimeout').value === '10000' && trigger.textContent === '10 秒',
            '自定义选项同步原生值',
        );
        trigger.click();
        trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
        trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
        assert(root.querySelector('#RequestTimeout').value === '60000', '键盘选择');
        trigger.click();
        trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
        assert(trigger.getAttribute('aria-expanded') === 'false', 'Escape 关闭');
        root.querySelector('#RequestTimeout').value = '10000';
        customSelect.refresh();
        root.querySelector('#bangumiConfigurationForm').requestSubmit();
        await tick();
        assert(
            root.querySelector('[type=submit]').closest('bangumi-button')?.getAttribute('variant') === 'primary',
            '保存使用主要按钮组件',
        );
        assert(
            !root.querySelector('[type=submit]').disabled &&
                !root.querySelector('[type=submit]').hasAttribute('aria-busy'),
            '保存后按钮恢复',
        );
        assert(saveBar.hidden, '保存成功隐藏保存栏');
        assert(saved.RequestTimeout === 10000 && saved.FutureSetting.preserve, '保存类型与未知字段保留');
        saved = null;
        root.querySelector('#BaseServerUrl').value = 'invalid url';
        root.querySelector('#bangumiConfigurationForm').requestSubmit();
        await tick();
        assert(!saved, '无效地址禁止保存');
        root.querySelector('#BaseServerUrl').value = config.BaseServerUrl;
        root.querySelector('[data-target=account]').click();
        const accountRoot = root.querySelector('bangumi-oauth-container').shadowRoot;
        assert(accountRoot && !root.querySelector('#bangumi-oauth-btn'), '授权区独立 Shadow DOM');
        root.querySelector('bangumi-oauth-container').shadowRoot.querySelector('#bangumi-jellyfin-user-switch').click();
        assert(
            !root.querySelector('bangumi-oauth-container').shadowRoot.querySelector('#bangumi-jellyfin-user-menu')
                .hidden,
            '用户菜单在 Shadow DOM 内保持打开',
        );
        root.querySelector('[data-target=episode-parser]').click();
        const versionsSwitch = root.querySelector('#MergeEpisodeVersionsByBangumiId');
        assert(!versionsSwitch.checked, 'Bangumi 版本合并默认关闭');
        assert(
            root.querySelector('#MergeEpisodeVersionsByBangumiId-description strong').textContent.includes('临时'),
            '版本合并标记为临时修复',
        );
        versionsSwitch.click();
        root.querySelector('#bangumiConfigurationForm').requestSubmit();
        await tick();
        assert(saved.MergeEpisodeVersionsByBangumiId === true, '通用版本合并开关保存');
        const parser = root.querySelector('#EpisodeParser');
        const segments = parser.closest('bangumi-segmented-select').shadowRoot;
        assert(segments.querySelector('input:checked').value === config.EpisodeParser, '解析器分段选择回填');
        segments.querySelector('input[value=Basic]').click();
        assert(
            parser.value === 'Basic' && root.querySelector('[episode-parser=Basic]').style.display !== 'none',
            '分段选择与解析器选项联动',
        );
        segments.querySelector('input[value=Torrent]').click();
        assert(parser.value === 'Torrent', '恢复混合解析器');
        assert(versionsSwitch.checked && !versionsSwitch.closest('[episode-parser]'), '版本合并不依赖解析模式');

        const firstTab = root.querySelector('[role=tab]');
        firstTab.click();
        firstTab.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
        assert(
            root.querySelector('[data-tab=tabRegexTools]').getAttribute('aria-selected') === 'true',
            '正则标签页键盘切换',
        );

        root.querySelector('#RegexToolGenerateBtn').click();
        assert(root.querySelector('#RegexToolPatternOutput').value.includes('\\('), '正则生成');
        root.querySelector('[data-target=media-library]').click();
        await tick();
        assert(
            root
                .querySelector('#bangumi-media-library-select')
                .closest('bangumi-select')
                .shadowRoot.querySelector('button').textContent === '全部媒体库',
            '动态下拉选项刷新',
        );
        assert(
            root.querySelector('.bangumi-media-list-edit').closest('bangumi-button')?.hasAttribute('icon'),
            '媒体库配置操作使用图标按钮',
        );
        root.querySelector('.bangumi-media-list-edit').click();
        await tick();
        let dialog = root.querySelector('dialog');
        assert(dialog?.open && dialog.querySelector('#bangumi-media-config-id').value === '1', '媒体库弹窗读取');
        const typeSelect = dialog.querySelector('#bangumi-media-config-directory-type');
        const typeSegments = typeSelect.closest('bangumi-segmented-select').shadowRoot;
        assert(
            typeSelect.value === 'Auto' && typeSegments.querySelector('input:checked').value === 'Auto',
            '旧配置默认 Auto',
        );
        typeSegments.querySelector('input[value=Normal]').click();
        assert(typeSelect.value === 'Normal', '目录类型分段选择同步');
        const preview = dialog.querySelector('bangumi-episode-preview').shadowRoot;
        assert(dialog.querySelector('#bangumi-media-offset-options').hidden, '无偏移隐藏映射选项');
        assert(preview.querySelector('#detected').textContent === '27', '随机剧集预览');
        const sampleRequests = toolCalls.filter((call) => call.url.includes('/MediaLibrary/Preview/')).length;
        dialog.querySelector('#bangumi-media-config-offset').value = '26';
        dialog.querySelector('#bangumi-media-config-offset').dispatchEvent(new Event('input', { bubbles: true }));
        assert(!dialog.querySelector('#bangumi-media-offset-options').hidden, '非零偏移显示映射选项');
        assert(
            preview.querySelector('#bangumi').textContent === '1' &&
                preview.querySelector('#jellyfin').textContent === '27',
            '偏移预览',
        );
        dialog.querySelector('#bangumi-media-config-correct-index').click();
        assert(preview.querySelector('#jellyfin').textContent === '1', '修正集数预览');
        assert(
            toolCalls.filter((call) => call.url.includes('/MediaLibrary/Preview/')).length === sampleRequests,
            '偏移和修正仅本地计算',
        );

        dialog.querySelector('form').requestSubmit();
        await tick();
        assert(directoryType === 'Normal', '目录类型随配置保存');
        assert(!root.querySelector('dialog'), '媒体库弹窗保存关闭清理');
        root.querySelector('.bangumi-media-list-edit').click();
        await tick();
        assert(root.querySelector('dialog')?.open, '媒体库弹窗可以重新打开');
        dialog = root.querySelector('dialog');
        assert(dialog.querySelector('#bangumi-media-config-directory-type').value === 'Normal', '已保存类型重新回填');
        dialog
            .querySelector('#bangumi-media-config-directory-type')
            .closest('bangumi-segmented-select')
            .shadowRoot.querySelector('input[value=Special]')
            .click();
        dialog.querySelector('form').requestSubmit();
        await tick();
        assert(directoryType === 'Special', '特典类型保存');
        root.querySelector('.bangumi-media-list-edit').click();
        await tick();
        assert(
            root.querySelector('dialog').querySelector('#bangumi-media-config-directory-type').value === 'Special',
            '特典类型重新回填',
        );
        root.querySelector('dialog').close();
        await tick();
        assert(!root.querySelector('dialog'), '原生关闭弹窗清理');
        root.querySelector('[data-target=tools]').click();
        assert(root.querySelector('.submit-button-container').hidden, '工具页不显示保存');
        const toolsRoot = root.querySelector('bangumi-tools').shadowRoot;
        const cards = toolsRoot.querySelectorAll('.card');
        cards[0].click();
        let tool = toolsRoot.querySelector('bangumi-tool-duplicates').shadowRoot;
        tool.querySelector('#scan').click();
        await tick();
        assert(tool.querySelector('#results input'), '重复剧集扫描');
        assert(
            tool.querySelectorAll('#results input:checked').length === 2 &&
                Array.from(tool.querySelectorAll('#results input')).find((input) => input.value === 'file-2')
                    ?.checked === false,
            '默认只保留首个最新文件',
        );
        assert(
            tool.querySelector('.episode-details-link').target === '_blank' &&
                tool.querySelector('.episode-details-link').hash === '#/details?id=file-1',
            '视频详情新窗口链接',
        );
        tool.querySelector('#results input').checked = true;
        tool.querySelector('#delete').click();
        await tick();
        assert(!toolCalls.some((call) => call.url.endsWith('/Delete')), '取消删除不发送请求');
        confirmed = true;
        tool.querySelector('#delete').click();
        await tick();
        assert(
            toolCalls.some((call) => call.url.endsWith('/Delete') && call.data.items === 'file-1,file-3'),
            '删除仅提交勾选 ID',
        );
        toolsRoot.querySelector('#back').click();
        await tick();
        cards[1].click();
        tool = toolsRoot.querySelector('bangumi-tool-fix-metadata').shadowRoot;
        tool.querySelector('#run').click();
        await tick();
        assert(tool.querySelector('[role=status]').textContent.includes('移除 2'), '元数据修正结果');
        toolsRoot.querySelector('#back').click();
        await tick();
        cards[2].click();
        tool = toolsRoot.querySelector('bangumi-tool-missing-id').shadowRoot;
        await tick();
        assert(tool.querySelector('#library').options.length === 2, '缺失 ID 工具加载媒体库');
        assert(getComputedStyle(tool.querySelector('#library')).opacity === '0', '工具内部原生下拉由组件隐藏');
        tool.querySelector('#library').value = 'name:Anime';
        tool.querySelector('#library').dispatchEvent(new Event('change'));
        tool.querySelector('#scan').click();
        await tick();
        assert(
            toolCalls.some((call) => call.url.includes('MissingBangumiId/Items?libraryId=name%3AAnime')),
            '扫描传递所选媒体库',
        );
        assert(
            tool.querySelectorAll('.missing-series').length === 1 &&
                tool.querySelectorAll('.missing-season').length === 1,
            '相同系列季度合并分组',
        );
        assert(tool.querySelector('bangumi-checkbox-group').children.length === 2, '同季视频组成 Checkbox Group');
        assert(tool.querySelector('.result-group-header a').hash === '#/details?id=season-1', '单季详情链接');
        assert(
            tool.querySelector('.result-group-header h3').textContent === '第一季' &&
                !tool.querySelector('.missing-season h4'),
            '单季只展示季度标题',
        );
        assert(
            tool.querySelector('bangumi-checkbox a[slot=actions]').target === '_blank',
            '视频详情位于行内并新窗口打开',
        );
        assert(tool.querySelector('input[data-unavailable]').disabled, '未启用提供程序不可刷新');
        tool.querySelector('input:not([data-unavailable])').checked = true;
        tool.querySelector('#refresh').click();
        await tick();
        assert(tool.querySelectorAll('input[data-unavailable]:disabled').length === 2, '刷新排队后不可重复选择');
        confirmed = false;
        toolsRoot.querySelector('#back').click();
        await tick();
        root.querySelector('[data-target=network]').click();
        assert(root.querySelector('.submit-button-container').hidden, '返回未修改设置不显示保存');
        dirtyField.click();
        root.querySelector('[data-target=metadata]').click();
        assert(!saveBar.hidden, '跨设置页保留未保存状态');
        dirtyField.click();
        assert(saveBar.hidden, '跨页还原后隐藏保存');
        const before = loads;
        document.querySelector('#page').dispatchEvent(new Event('viewshow'));
        document.querySelector('#page').dispatchEvent(new Event('pageshow'));
        await tick();
        assert(loads === before, '重复显示事件不重复加载');
        app.remove();
        document.querySelector('#page').append(app);
        await tick();
        assert(loads === before + 1, '重新挂载加载一次');
        document.querySelector('#page').dispatchEvent(new Event('viewhide'));
        document.querySelector('#page').dispatchEvent(new Event('viewshow'));
        await tick();
        assert(loads === before + 2, '缓存页面重进可恢复');
        report.textContent =
            'PASS：工具扫描/确认/修正/刷新、自定义下拉/键盘/动态选项、Checkbox 标签/禁用/状态、样式隔离、保存类型、未知字段、校验、菜单、正则、媒体库弹窗、重复事件、重新挂载、缓存恢复';
    } catch (error) {
        report.textContent = 'FAIL：' + error.message;
    }
};
