import { pushSectionInUrl } from './navigation-state.ts';
import { collectConfiguration } from './configuration.ts';

export function createController(container, host) {
    var pluginId = '41b59f1b-a6cf-474a-b416-785379cbd856';
    const { api: ApiClient, dashboard: Dashboard } = host;
    let active = false;
    let loaded = false;
    let saving = false;
    var configuration = {};
    let savedSnapshot = '';
    const currentConfiguration = () =>
        collectConfiguration(configuration, container.querySelectorAll('input,select,textarea'));
    function updateSaveBar() {
        const dirty = loaded && JSON.stringify(currentConfiguration()) !== savedSnapshot;
        const section = getDefaultModule();
        container.querySelector('.submit-button-container').hidden =
            ['tools', 'media-library'].includes(section) || (!dirty && !saving);
    }
    container.addEventListener('input', updateSaveBar);
    container.addEventListener('change', updateSaveBar);
    // Reset buttons assign values directly, without native input/change events.
    container.addEventListener('click', () => queueMicrotask(updateSaveBar));
    const account = container.querySelector(
        'bangumi-oauth-container',
    ) as import('./components/account.ts').BangumiOAuthContainer;
    account.configure(host);
    var mediaLibraryState = {
        initialized: false,
        librariesLoaded: false,
        startIndex: 0,
        pageSize: 100,
        totalRecordCount: 0,
        totalItemCount: 0,
        rootItems: [],
        currentDirectory: null,
        selectedItemId: '',
        searchTimer: 0,
        searchComposing: false,
        requestId: 0,
        dialog: null,
        dialogHelper: null,
        dialogPromise: null,
    };

    function getAvailableModules() {
        return (Array.from(container.querySelectorAll('.bangumi-settings-nav-item')) as HTMLElement[])
            .map(function (button) {
                return button.getAttribute('data-target');
            })
            .filter((module) => host.modules.includes(module));
    }

    function getDefaultModule() {
        var activeButton = container.querySelector('.bangumi-settings-nav-item.active');
        return activeButton ? activeButton.getAttribute('data-target') : 'account';
    }

    function getModuleFromHash() {
        var hash = window.location.hash || '';
        var queryIndex = hash.indexOf('?');
        if (queryIndex === -1) {
            return '';
        }

        var search = hash.substring(queryIndex + 1);
        var params = new URLSearchParams(search);
        return params.get('module') || '';
    }

    function getResolvedModule(module) {
        var availableModules = getAvailableModules();
        return availableModules.indexOf(module) !== -1 ? module : getDefaultModule();
    }

    function applyModuleFromHash() {
        switchSettingsSection(getResolvedModule(getModuleFromHash()), false);
        container.querySelector('bangumi-tools')?.syncRoute();
    }

    function loadArchiveState() {
        if (!host.modules.includes('archive')) return Promise.resolve();
        return ApiClient.getJSON(ApiClient.getUrl('/Plugins/Bangumi/Archive/Status')).then(function (data) {
            const size = Number(data?.size) || 0;
            const hasData = size > 0;
            container.querySelector('#bangumi-archive-container').classList.toggle('has-archive-data', hasData);
            container.querySelector('#archive-status-title').textContent = hasData
                ? '离线数据库已就绪'
                : '尚未下载数据库';
            container.querySelector('#archive-status-description').textContent = hasData
                ? '优先查询本地数据，近期剧集按下方设置使用在线接口。'
                : '当前使用在线接口。可前往计划任务下载数据库并设置更新频率。';
            const units = ['B', 'KB', 'MB', 'GB', 'TB'];
            const index = hasData ? Math.min(Math.floor(Math.log2(size) / 10), units.length - 1) : 0;
            container.querySelector('#archive-size').textContent = hasData
                ? (size / Math.pow(1024, index)).toFixed(2) + ' ' + units[index]
                : '—';
            container.querySelector('#archive-folder').textContent = data?.path || '—';
            const time = data?.time ? new Date(data.time) : null;
            container.querySelector('#archive-update-time').textContent =
                time && !Number.isNaN(time.getTime())
                    ? new Intl.DateTimeFormat('zh-Hans', { dateStyle: 'medium', timeStyle: 'short' }).format(time)
                    : '—';

            return ApiClient.getScheduledTasks().then(function (tasks) {
                var task = tasks.find(function (task) {
                    return task.Key === 'ArchiveDataDownloadTask' && task.Category === 'Bangumi';
                });
                const button = container.querySelector('#config-archive-update-task');
                button.disabled = !task;
                button.title = task ? '' : '未找到离线数据库更新任务';
                button.onclick = task
                    ? () => {
                          window.location.hash = '#/dashboard/tasks/' + task.Id;
                      }
                    : null;
            });
        });
    }

    function loadConfiguration() {
        return ApiClient.getPluginConfiguration(pluginId).then(function (config) {
            configuration = config;
            Object.keys(config).forEach(function (configKey) {
                var element = container.querySelector('#' + configKey);
                if (!element) return;
                if (element.type === 'checkbox') {
                    element.checked = config[configKey];
                } else {
                    element.value = config[configKey];
                    element.closest('bangumi-select, bangumi-segmented-select')?.refresh();
                }
            });

            if (container.querySelector('#EpisodeParser')) {
                updateEpisodeParserDisplay();
            }

            updateNSFWReportDisplay();
            loaded = true;
            savedSnapshot = JSON.stringify(currentConfiguration());
            updateSaveBar();
            container.querySelector('[type=submit]').disabled = false;
        });
    }

    function updateNSFWReportDisplay() {
        var skipNSFWReport = container.querySelector('#SkipNSFWPlaybackReport');
        var privateNSFWReport = container.querySelector('#PrivateNSFWPlaybackReport');
        if (!skipNSFWReport || !privateNSFWReport) return;

        privateNSFWReport.disabled = skipNSFWReport.checked;
    }

    function saveConfiguration() {
        if (!loaded || saving) return;
        saving = true;
        const saveButton = container.querySelector('[type=submit]');
        saveButton.disabled = true;
        saveButton.setAttribute('aria-busy', 'true');
        saveButton.textContent = '保存中…';
        const config = currentConfiguration();
        return wrapLoading(
            ApiClient.updatePluginConfiguration(pluginId, config)
                .then((result) => {
                    configuration = config;
                    savedSnapshot = JSON.stringify(config);
                    Dashboard.processPluginConfigurationUpdateResult(result);
                })
                .finally(() => {
                    saving = false;
                    saveButton.disabled = false;
                    saveButton.removeAttribute('aria-busy');
                    saveButton.textContent = '保存';
                    updateSaveBar();
                }),
        );
    }

    function onLoad() {
        if (active) return;
        active = true;
        window.addEventListener('hashchange', applyModuleFromHash);
        window.addEventListener('popstate', applyModuleFromHash);
        applyModuleFromHash();
        wrapLoading(Promise.all([loadConfiguration(), loadArchiveState(), account.show()]));
    }

    function onUnload() {
        active = false;
        window.clearTimeout(mediaLibraryState.searchTimer);
        closeMediaLibraryDialog();
        account.hide();
        window.removeEventListener('hashchange', applyModuleFromHash);
        window.removeEventListener('popstate', applyModuleFromHash);
    }

    function wrapLoading(promise) {
        Dashboard.showLoadingMsg();
        return promise
            .catch((error) => {
                if (active && error.name !== 'AbortError') Dashboard.alert('操作失败：' + (error.message || error));
            })
            .finally(() => Dashboard.hideLoadingMsg());
    }

    container.querySelector('[type=submit]').disabled = true;

    container.querySelector('#bangumiConfigurationForm').addEventListener('submit', function (e) {
        e.preventDefault();
        saveConfiguration();
    });

    container.querySelector('#SkipNSFWPlaybackReport').addEventListener('change', updateNSFWReportDisplay);

    container.querySelector('#delete-archive-data').addEventListener('click', function (e) {
        e.preventDefault();
        Dashboard.confirm('确定要清空离线数据库吗？', '警告', function (confirmed) {
            if (!confirmed) return;
            Dashboard.showLoadingMsg();
            wrapLoading(
                ApiClient.fetch({ url: '/Plugins/Bangumi/Archive/Store', type: 'DELETE' }).then(function () {
                    loadArchiveState();
                    Dashboard.alert('离线数据库已清空');
                }),
            );
        });
    });

    container.querySelector('#EpisodeParser').addEventListener('change', function (e) {
        e.preventDefault();
        updateEpisodeParserDisplay();
    });

    function getMediaLibraryApiUrl(path, query = {}) {
        var url = ApiClient.getUrl('/Plugins/Bangumi/Tools/MediaLibrary' + path);
        if (!query) {
            return url;
        }

        return url + '?' + new URLSearchParams(query).toString();
    }

    async function initializeMediaLibrary() {
        if (mediaLibraryState.initialized) {
            return;
        }

        mediaLibraryState.initialized = true;
        renderMediaLibraryItems();
        try {
            var response = await ApiClient.fetch({ type: 'GET', url: getMediaLibraryApiUrl('/Libraries') });
            if (!response.ok) throw new Error(await response.text());
            var libraries = await response.json();
            if (!active) return;
            loadMediaLibraryOptions(libraries);
        } catch (error) {
            mediaLibraryState.initialized = false;
            if (active && error.name !== 'AbortError') Dashboard.alert('加载媒体库列表失败：' + error.message);
        }
    }

    function loadMediaLibraryOptions(libraries) {
        if (mediaLibraryState.librariesLoaded) {
            return;
        }

        var select = container.querySelector('#bangumi-media-library-select');
        select.replaceChildren(new Option('请选择媒体库', ''), new Option('全部媒体库（手动加载）', '*'));
        libraries.forEach(function (libraryInfo) {
            select.appendChild(new Option(libraryInfo.Name || '未命名媒体库', libraryInfo.Id));
        });
        mediaLibraryState.librariesLoaded = true;
    }

    function updateMediaLibraryPagination() {
        var first = mediaLibraryState.totalRecordCount ? mediaLibraryState.startIndex + 1 : 0;
        var last = Math.min(
            mediaLibraryState.startIndex + mediaLibraryState.pageSize,
            mediaLibraryState.totalRecordCount,
        );
        container.querySelector('#bangumi-media-library-page-status').textContent =
            first + '–' + last + ' / ' + mediaLibraryState.totalRecordCount;
        container.querySelector('#bangumi-media-library-previous').disabled = mediaLibraryState.startIndex === 0;
        container.querySelector('#bangumi-media-library-next').disabled =
            mediaLibraryState.startIndex + mediaLibraryState.pageSize >= mediaLibraryState.totalRecordCount;
    }

    function enterMediaLibraryDirectory(item) {
        mediaLibraryState.currentDirectory = item;
        renderMediaLibraryItems();
    }

    function createMediaLibraryListItem(item) {
        var element = container.querySelector('#bangumi-media-library-item-template').content.cloneNode(true);
        var row = element.querySelector('.bangumi-media-list-item');
        var children = item.Children || [];
        var hasChildren = children.length > 0;
        var editButton = element.querySelector('.bangumi-media-list-edit');
        var enterButton = element.querySelector('.bangumi-media-list-enter');

        row.dataset.itemId = item.Id;
        element.querySelector('.bangumi-media-list-icon').textContent = hasChildren ? 'folder' : 'folder_open';
        element.querySelector('.bangumi-media-list-name').textContent = item.Name;
        element.querySelector('.bangumi-media-list-path').textContent = item.Path;
        row.dataset.configured = String(!!item.HasConfiguration);
        editButton.title = item.HasConfiguration ? '编辑单独配置' : '配置此文件夹（当前继承设置）';
        editButton.setAttribute('aria-label', editButton.title);
        element.querySelector('.bangumi-media-child-count').textContent = hasChildren
            ? children.length + ' 个子目录'
            : '';
        element.querySelector('.bangumi-media-list-path').title = item.Path;
        element
            .querySelector('.bangumi-media-list-main')
            .setAttribute('aria-label', (hasChildren ? '浏览：' : '配置：') + item.Name);
        enterButton.style.display = hasChildren ? '' : 'none';

        function activateDefaultAction() {
            if (hasChildren) {
                enterMediaLibraryDirectory(item);
            } else {
                openMediaLibraryEditor(item);
            }
        }

        row.addEventListener('click', function () {
            activateDefaultAction();
        });
        editButton.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();
            openMediaLibraryEditor(item);
        });
        enterButton.addEventListener('click', function (event) {
            event.preventDefault();
            event.stopPropagation();
            enterMediaLibraryDirectory(item);
        });

        return element;
    }

    function renderMediaLibraryItems() {
        var list = container.querySelector('#bangumi-media-library-list');
        var empty = container.querySelector('#bangumi-media-library-empty');
        var pagination = container.querySelector('#bangumi-media-library-pagination');
        var backButton = container.querySelector('#bangumi-media-library-back');
        var currentLabel = container.querySelector('#bangumi-media-library-current');
        var summary = container.querySelector('#bangumi-media-library-summary');
        var items = mediaLibraryState.currentDirectory
            ? mediaLibraryState.currentDirectory.Children || []
            : mediaLibraryState.rootItems;

        list.replaceChildren();
        items.forEach(function (item) {
            list.appendChild(createMediaLibraryListItem(item));
        });

        if (mediaLibraryState.currentDirectory) {
            backButton.style.display = '';
            currentLabel.textContent = mediaLibraryState.currentDirectory.Name;
            summary.textContent = '共 ' + items.length + ' 个实际媒体文件夹';
            pagination.style.display = 'none';
            empty.textContent = '此系列下没有已索引的媒体文件夹';
        } else {
            var select = container.querySelector('#bangumi-media-library-select');
            var selectedOption = select.options[select.selectedIndex];
            backButton.style.display = 'none';
            currentLabel.textContent = selectedOption ? selectedOption.textContent : '请选择媒体库';
            summary.textContent = '共找到 ' + mediaLibraryState.totalItemCount + ' 个可配置目录';
            pagination.style.display = mediaLibraryState.totalRecordCount > mediaLibraryState.pageSize ? '' : 'none';
            empty.textContent = select.value ? '当前筛选条件下没有可配置的系列目录' : '请选择媒体库后加载目录';
            if (!select.value) summary.textContent = '';
            updateMediaLibraryPagination();
        }

        empty.hidden = items.length > 0;
        list.style.display = items.length ? '' : 'none';
    }

    async function loadMediaLibraryItems() {
        var requestId = ++mediaLibraryState.requestId;
        var select = container.querySelector('#bangumi-media-library-select');
        var search = container.querySelector('#bangumi-media-library-search');
        if (!select.value) {
            mediaLibraryState.rootItems = [];
            mediaLibraryState.currentDirectory = null;
            mediaLibraryState.totalRecordCount = 0;
            mediaLibraryState.totalItemCount = 0;
            renderMediaLibraryItems();
            Dashboard.hideLoadingMsg();
            return;
        }

        Dashboard.showLoadingMsg();
        try {
            var response = await ApiClient.fetch({
                type: 'GET',
                url: getMediaLibraryApiUrl('/Items', {
                    libraryId: select.value === '*' ? '' : select.value,
                    search: search.value.trim(),
                    startIndex: String(mediaLibraryState.startIndex),
                    limit: String(mediaLibraryState.pageSize),
                }),
            });
            if (!response.ok) {
                throw new Error(await response.text());
            }

            var result = await response.json();
            if (requestId !== mediaLibraryState.requestId) {
                return;
            }

            mediaLibraryState.totalRecordCount = result.TotalRecordCount;
            mediaLibraryState.totalItemCount = result.TotalItemCount;
            mediaLibraryState.rootItems = result.Items;
            loadMediaLibraryOptions(result.Libraries);
            if (mediaLibraryState.currentDirectory) {
                mediaLibraryState.currentDirectory =
                    mediaLibraryState.rootItems.find(function (item) {
                        return item.Id === mediaLibraryState.currentDirectory.Id;
                    }) || null;
            }
            renderMediaLibraryItems();
        } catch (error) {
            if (!active || requestId !== mediaLibraryState.requestId || error.name === 'AbortError') return;
            Dashboard.alert('加载媒体库失败：' + error.message);
        } finally {
            if (requestId === mediaLibraryState.requestId) {
                Dashboard.hideLoadingMsg();
            }
        }
    }

    function updateMediaLibraryConfigFields() {
        var dialog = mediaLibraryState.dialog;
        if (!dialog) {
            return;
        }

        var enabled = dialog.querySelector('#bangumi-media-config-enabled').checked;
        dialog.querySelector('#bangumi-media-config-fields').style.display = enabled ? '' : 'none';
        const offset = Number(dialog.querySelector('#bangumi-media-config-offset').value);
        dialog.querySelector('#bangumi-media-offset-options').hidden = !Number.isFinite(offset) || offset === 0;
    }

    function closeMediaLibraryDialog() {
        if (mediaLibraryState.dialog && mediaLibraryState.dialogHelper) {
            mediaLibraryState.dialogHelper.close(mediaLibraryState.dialog);
        }
    }

    function createMediaLibraryDialog() {
        if (mediaLibraryState.dialogPromise) {
            return mediaLibraryState.dialogPromise;
        }

        try {
            var dialogHelper = host.dialogHelper;
            if (!dialogHelper || typeof dialogHelper.createDialog !== 'function') {
                throw new Error('无法初始化配置对话框');
            }

            var template = container.querySelector('#bangumi-media-library-dialog-template');
            var dialog = dialogHelper.createDialog({
                id: 'bangumi-media-library-dialog',
                size: 'small',
                removeOnClose: true,
            });

            dialog.classList.add('formDialog');
            dialog.appendChild(template.content.cloneNode(true));
            dialog
                .querySelector('#bangumi-media-config-enabled')
                .addEventListener('change', updateMediaLibraryConfigFields);
            dialog
                .querySelector('#bangumi-media-config-offset')
                .addEventListener('input', updateMediaLibraryConfigFields);
            dialog
                .querySelector('#bangumi-media-config-offset')
                .addEventListener('change', updateMediaLibraryConfigFields);
            dialog.querySelectorAll('.btnCancel').forEach(function (button) {
                button.addEventListener('click', closeMediaLibraryDialog);
            });
            dialog.querySelector('form').addEventListener('submit', function (event) {
                event.preventDefault();
                saveMediaLibraryConfiguration();
            });
            dialog.addEventListener(
                'close',
                function () {
                    if (mediaLibraryState.dialog === dialog) {
                        mediaLibraryState.dialog = null;
                        mediaLibraryState.dialogHelper = null;
                        mediaLibraryState.dialogPromise = null;
                    }
                },
                { once: true },
            );

            mediaLibraryState.dialog = dialog;
            mediaLibraryState.dialogHelper = dialogHelper;
            mediaLibraryState.dialogPromise = Promise.resolve(dialog);
        } catch (error) {
            mediaLibraryState.dialogPromise = null;
            return Promise.reject(error);
        }

        return mediaLibraryState.dialogPromise;
    }

    async function openMediaLibraryEditor(item) {
        Dashboard.showLoadingMsg();
        try {
            var response = await ApiClient.fetch({
                type: 'GET',
                url: getMediaLibraryApiUrl('/Configuration/' + item.Id),
            });
            if (!response.ok) {
                throw new Error(await response.text());
            }

            var config = await response.json();
            var dialog = await createMediaLibraryDialog();
            mediaLibraryState.selectedItemId = config.ItemId;
            dialog.querySelector('#bangumi-media-dialog-title').textContent = '配置：' + config.ItemName;
            dialog.querySelector('#bangumi-media-dialog-path').textContent = config.DirectoryPath;
            dialog.querySelector('#bangumi-media-config-enabled').checked = config.Exists;
            dialog.querySelector('#bangumi-media-config-id').value = config.Id || '';
            dialog.querySelector('#bangumi-media-config-offset').value = config.Offset || '';
            var directoryType = dialog.querySelector('#bangumi-media-config-directory-type');
            directoryType.value = config.Type || 'Auto';
            directoryType.closest('bangumi-segmented-select').refresh();
            dialog.querySelector('#bangumi-media-config-report').checked = config.Report;
            dialog.querySelector('#bangumi-media-config-skip').checked = config.Skip;
            dialog.querySelector('#bangumi-media-config-correct-index').checked = config.CorrectIndex;
            updateMediaLibraryConfigFields();
            mediaLibraryState.dialogHelper.open(dialog);
            dialog
                .querySelector('bangumi-episode-preview')
                .configure(ApiClient, config.ItemId, dialog.querySelector('form'));
        } catch (error) {
            Dashboard.alert('读取 bangumi.ini 失败：' + error.message);
        } finally {
            Dashboard.hideLoadingMsg();
        }
    }

    function getMediaLibraryConfigurationPayload() {
        var dialog = mediaLibraryState.dialog;
        return {
            Id: Number.parseInt(dialog.querySelector('#bangumi-media-config-id').value || '0', 10),
            Offset: Number.parseInt(dialog.querySelector('#bangumi-media-config-offset').value || '0', 10),
            Report: dialog.querySelector('#bangumi-media-config-report').checked,
            Skip: dialog.querySelector('#bangumi-media-config-skip').checked,
            CorrectIndex: dialog.querySelector('#bangumi-media-config-correct-index').checked,
            Type: dialog.querySelector('#bangumi-media-config-directory-type').value,
        };
    }

    async function saveMediaLibraryConfiguration() {
        var dialog = mediaLibraryState.dialog;
        if (!dialog) {
            return;
        }

        var enabled = dialog.querySelector('#bangumi-media-config-enabled').checked;
        var idInput = dialog.querySelector('#bangumi-media-config-id');
        var offsetInput = dialog.querySelector('#bangumi-media-config-offset');
        if (enabled && (!idInput.reportValidity() || !offsetInput.reportValidity())) {
            return;
        }

        Dashboard.showLoadingMsg();
        try {
            var response = enabled
                ? await ApiClient.fetch({
                      type: 'PUT',
                      url: getMediaLibraryApiUrl('/Configuration/' + mediaLibraryState.selectedItemId),
                      contentType: 'application/json',
                      data: JSON.stringify(getMediaLibraryConfigurationPayload()),
                  })
                : await ApiClient.fetch({
                      type: 'DELETE',
                      url: getMediaLibraryApiUrl('/Configuration/' + mediaLibraryState.selectedItemId),
                  });
            if (!response.ok) {
                throw new Error(await response.text());
            }

            closeMediaLibraryDialog();
            await loadMediaLibraryItems();
            Dashboard.alert(enabled ? 'bangumi.ini 已保存。' : 'bangumi.ini 已删除。');
        } catch (error) {
            Dashboard.alert('更新 bangumi.ini 失败：' + error.message);
        } finally {
            Dashboard.hideLoadingMsg();
        }
    }

    function switchSettingsSection(target, updateUrl = true) {
        if (!target) {
            return;
        }

        var resolvedTarget = getResolvedModule(target);
        if (updateUrl) {
            pushSectionInUrl(resolvedTarget, window.location, window.history);
            container.querySelector('bangumi-tools')?.syncRoute();
        }

        container.querySelectorAll('.bangumi-settings-nav-item').forEach(function (button) {
            const active = button.getAttribute('data-target') === resolvedTarget;
            button.classList.toggle('active', active);
            if (active) button.setAttribute('aria-current', 'page');
            else button.removeAttribute('aria-current');
        });

        container.querySelectorAll('.bangumi-settings-panel').forEach(function (panel) {
            panel.classList.toggle('active', panel.getAttribute('data-section') === resolvedTarget);
        });

        updateSaveBar();

        if (resolvedTarget === 'media-library') {
            initializeMediaLibrary();
        }
    }

    function updateEpisodeParserDisplay() {
        const parser = container.querySelector('#EpisodeParser').value;
        container.querySelectorAll('.episode-parser-options').forEach((el) => {
            el.style.display = el.getAttribute('episode-parser') === parser ? '' : 'none';
        });
        const hybridSection = container.querySelector('.bangumi-tab-container');
        if (hybridSection) {
            hybridSection.style.display = parser === 'Torrent' ? '' : 'none';
        }
    }

    /**
     * 对文本做正则转义，生成可直接匹配原始文本的正则表达式
     *
     * @param {string} value 原始文本。
     * @returns {string} 转义后的正则表达式
     */
    function escapeRegex(value) {
        return (value || '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    /**
     * 归一化输入文本，去除首尾空白字符。
     *
     * @param {string} value 用户输入的文本。
     * @returns {string} 清理后的文本。
     */
    function normalizeInputText(value) {
        return (value || '').trim();
    }

    /**
     * 移除路径末尾连续的斜杠或反斜杠。
     *
     * @param {string} value 完整路径。
     * @returns {string} 去除结尾分隔符后的路径。
     */
    function trimTrailingSlashes(value) {
        return value.replace(/[\\/]+$/, '');
    }

    /**
     * 将路径按目录分隔符拆分为有效片段。
     *
     * @param {string} fullPath 完整路径。
     * @returns {string[]} 路径片段数组。
     */
    function getPathSegments(fullPath) {
        return fullPath.split(/[\\/]+/).filter(Boolean);
    }

    /**
     * 根据路径类型解析完整路径、目录名和文件名输入。
     *
     * @param {string} fullPath 用户输入的完整路径。
     * @param {string} pathType 当前测试的路径类型。
     * @returns {{fullPath: string, folderName: string, fileName: string}} 解析结果。
     */
    function resolvePathInputs(fullPath, pathType) {
        var normalized = normalizeInputText(fullPath);
        var trimmed = trimTrailingSlashes(normalized);
        var segments = getPathSegments(trimmed);
        var folderName = '';
        var fileName = '';

        // 按路径类型提取用于匹配的目录名和文件名。
        if (pathType === 'SeasonFolder') {
            folderName = segments.length ? segments[segments.length - 1] : '';
        } else {
            fileName = segments.length ? segments[segments.length - 1] : '';
            folderName = segments.length > 1 ? segments[segments.length - 2] : '';
        }

        return {
            fullPath: normalized,
            folderName: folderName,
            fileName: fileName,
        };
    }

    /**
     * 将多行正则输入拆分为逐行规则，并过滤空行。
     *
     * @param {string} value 多行正则文本。
     * @returns {string[]} 有效的正则规则列表。
     */
    function getRegexLines(value) {
        return (value || '')
            .split(/\r?\n/)
            .map((l) => l.trim())
            .filter(Boolean);
    }

    /**
     * 逐行测试正则规则，返回首个命中项和无效规则信息。
     *
     * @param {string} patternText 多行正则文本。
     * @param {string} inputValue 待测试的输入值。
     * @returns {{matched: {lineNumber: number, pattern: string}|null, invalid: {lineNumber: number, pattern: string, message: string}[]}} 测试结果。
     */
    function testRegexLines(patternText, inputValue) {
        var patterns = getRegexLines(patternText);
        var invalid = [];

        if (!inputValue) {
            return { matched: null, invalid: invalid };
        }

        // 按顺序测试规则，并记录所有语法错误的正则。
        for (var i = 0; i < patterns.length; i++) {
            var pattern = patterns[i];
            try {
                if (new RegExp(pattern, 'i').test(inputValue)) {
                    return {
                        matched: { lineNumber: i + 1, pattern: pattern },
                        invalid: invalid,
                    };
                }
            } catch (error) {
                invalid.push({
                    lineNumber: i + 1,
                    pattern: pattern,
                    message: error && error.message ? error.message : '无效正则',
                });
            }
        }

        return { matched: null, invalid: invalid };
    }

    /**
     * 将用户输入的普通文本转换成正则表达式，并输出到结果面板。
     *
     * @returns {boolean} 是否成功生成输出。
     */
    function buildRegexToolOutputs() {
        var source = normalizeInputText(container.querySelector('#RegexToolPathInput').value);
        var patternOutput = container.querySelector('#RegexToolPatternOutput');

        if (!source) {
            patternOutput.value = '';
            return false;
        }

        var pattern = escapeRegex(source);
        patternOutput.value = pattern;
        return true;
    }

    /**
     * 复制文本到剪贴板，并在完成后给出提示。
     *
     * @param {string} value 要复制的文本。
     * @param {string} successMessage 复制成功后的提示语。
     * @returns {Promise<boolean>} 是否复制成功。
     */
    function copyText(value, successMessage) {
        if (!value) {
            Dashboard.alert('没有可复制的内容');
            return Promise.resolve(false);
        }

        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(value).then(
                () => {
                    Dashboard.alert(successMessage);
                    return true;
                },
                () => {
                    Dashboard.alert('复制失败，请手动复制');
                    return false;
                },
            );
        }

        Dashboard.alert('复制失败，请手动复制');
        return Promise.resolve(false);
    }

    /**
     * 将结果文本数组格式化为结果面板的 HTML 片段。
     *
     * @param {string[]} lines 结果文本列表。
     * @returns {string} 拼接后的 HTML 字符串。
     */
    function formatResultLines(lines) {
        var frag = document.createDocumentFragment();
        (lines || []).forEach((line) => {
            var div = document.createElement('div');
            div.className = 'bangumi-regex-test-result-line';
            div.textContent = String(line ?? '');
            frag.appendChild(div);
        });
        return frag;
    }

    /**
     * 渲染正则测试结果列表。
     *
     * @param {{title: string, state: string, lines: string[]}[]} items 待渲染的结果项。
     * @returns {void}
     */
    function renderRegexToolResults(items) {
        var results = container.querySelector('#RegexToolResults');
        results.innerHTML = '';
        (items || []).forEach((item) => {
            var wrapper = document.createElement('div');
            wrapper.className = 'bangumi-regex-test-result ' + (item.state || '');

            var title = document.createElement('div');
            title.className = 'bangumi-regex-test-result-title';
            title.textContent = item.title || '';
            wrapper.appendChild(title);

            wrapper.appendChild(formatResultLines(item.lines));

            results.appendChild(wrapper);
        });
    }

    /**
     * 运行正则工具测试，并将命中情况渲染到界面。
     *
     * @returns {void}
     */
    function runRegexToolTest() {
        var pathType = container.querySelector('#RegexToolPathType').value;
        var input = normalizeInputText(container.querySelector('#RegexToolTestInput').value);
        var resolvedFullPath = container.querySelector('#RegexToolResolvedFullPath');
        var resolvedFolderName = container.querySelector('#RegexToolResolvedFolderName');
        var resolvedFileName = container.querySelector('#RegexToolResolvedFileName');
        var summary = container.querySelector('#RegexToolSummary');
        var resultItems = [];

        // 设置拆分后的路径信息
        var resolved = resolvePathInputs(input, pathType);
        resolvedFullPath.textContent = resolved.fullPath || '-';
        resolvedFolderName.textContent = resolved.folderName || '-';
        resolvedFileName.textContent = resolved.fileName || '-';

        // 没有输入时直接显示空状态，避免继续执行后续测试逻辑。
        if (!input) {
            summary.textContent = '请输入待测试文本';
            renderRegexToolResults([{ title: '测试结果', state: 'miss', lines: ['当前没有输入任何文本'] }]);
            return;
        }

        // 获取相关正则配置，并构建测试项列表
        var episodeFileName = pathType === 'EpisodeFile' ? resolved.fileName : '';
        var groups = [
            {
                title: '排除白名单',
                inputs: [
                    {
                        label: '完整路径',
                        value: resolved.fullPath,
                        patterns: container.querySelector('#ExcludeWhitelistRegexFullPath').value,
                    },
                    {
                        label: '目录名称',
                        value: resolved.folderName,
                        patterns: container.querySelector('#ExcludeWhitelistRegexFolderName').value,
                    },
                    {
                        label: '文件名',
                        value: episodeFileName,
                        patterns: container.querySelector('#ExcludeWhitelistRegexFileName').value,
                    },
                ],
            },
            {
                title: '特典文件排除',
                inputs: [
                    {
                        label: '完整路径',
                        value: resolved.fullPath,
                        patterns: container.querySelector('#SpExcludeRegexFullPath').value,
                    },
                    {
                        label: '目录名称',
                        value: resolved.folderName,
                        patterns: container.querySelector('#SpExcludeRegexFolderName').value,
                    },
                    {
                        label: '文件名',
                        value: episodeFileName,
                        patterns: container.querySelector('#SpExcludeRegexFileName').value,
                    },
                ],
            },
            {
                title: '杂项文件排除',
                inputs: [
                    {
                        label: '完整路径',
                        value: resolved.fullPath,
                        patterns: container.querySelector('#MiscExcludeRegexFullPath').value,
                    },
                    {
                        label: '目录名称',
                        value: resolved.folderName,
                        patterns: container.querySelector('#MiscExcludeRegexFolderName').value,
                    },
                    {
                        label: '文件名',
                        value: episodeFileName,
                        patterns: container.querySelector('#MiscExcludeRegexFileName').value,
                    },
                ],
            },
        ];

        // 是否命中
        var whitelistMatched = false;
        var specialMatched = false;
        var miscMatched = false;

        // 汇总每个正则组的命中结果和无效正则
        groups.forEach((group) => {
            var matchedLine = null;
            var invalidLines = [];
            var lines = [];

            // 逐条测试输入项，记录命中和无效正则
            group.inputs.forEach((item) => {
                var testResult = testRegexLines(item.patterns, item.value);

                // 记录无效正则
                invalidLines = invalidLines.concat(
                    testResult.invalid.map((invalidItem) => {
                        return (
                            item.label +
                            ' 第 ' +
                            invalidItem.lineNumber +
                            ' 行无效: ' +
                            invalidItem.pattern +
                            ' (' +
                            invalidItem.message +
                            ')'
                        );
                    }),
                );

                // 记录首个命中正则
                if (!matchedLine && testResult.matched) {
                    matchedLine =
                        item.label + ' 第 ' + testResult.matched.lineNumber + ' 行命中: ' + testResult.matched.pattern;
                }
            });

            if (matchedLine) {
                lines.push(matchedLine);
            } else {
                lines.push('未命中任何正则');
            }

            if (invalidLines.length) {
                lines = lines.concat(invalidLines);
            }

            // 记录命中的正则类型
            if (group.title === '排除白名单' && matchedLine) {
                whitelistMatched = true;
            }
            if (group.title === '特典文件排除' && matchedLine) {
                specialMatched = true;
            }
            if (group.title === '杂项文件排除' && matchedLine) {
                miscMatched = true;
            }

            resultItems.push({
                title: group.title,
                state: matchedLine ? 'hit' : invalidLines.length ? 'invalid' : 'miss',
                lines: lines,
            });
        });

        // 根据各组命中情况生成摘要。
        var matchTypes = [];
        if (whitelistMatched) {
            matchTypes.push('白名单');
        }
        if (specialMatched) {
            matchTypes.push('特典');
        }
        if (miscMatched) {
            matchTypes.push('杂项');
        }
        if (matchTypes.length == 0) {
            matchTypes.push('无');
        }
        summary.textContent = '命中类型: ' + matchTypes.join(', ');

        renderRegexToolResults(resultItems);
    }

    container.querySelector('#SpExcludeRegexFullPathResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#SpExcludeRegexFullPath').value = configuration['DefaultSpExcludeRegexFullPath'];
    });

    container.querySelector('#SpExcludeRegexFolderNameResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#SpExcludeRegexFolderName').value = configuration['DefaultSpExcludeRegexFolderName'];
    });

    container.querySelector('#SpExcludeRegexFileNameResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#SpExcludeRegexFileName').value = configuration['DefaultSpExcludeRegexFileName'];
    });

    container.querySelector('#MiscExcludeRegexFullPathResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#MiscExcludeRegexFullPath').value = configuration['DefaultMiscExcludeRegexFullPath'];
    });

    container.querySelector('#MiscExcludeRegexFolderNameResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#MiscExcludeRegexFolderName').value =
            configuration['DefaultMiscExcludeRegexFolderName'];
    });

    container.querySelector('#MiscExcludeRegexFileNameResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#MiscExcludeRegexFileName').value = configuration['DefaultMiscExcludeRegexFileName'];
    });

    container.querySelector('#ExcludeWhitelistRegexFullPathResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#ExcludeWhitelistRegexFullPath').value =
            configuration['DefaultExcludeWhitelistRegexFullPath'];
    });

    container.querySelector('#ExcludeWhitelistRegexFolderNameResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#ExcludeWhitelistRegexFolderName').value =
            configuration['DefaultExcludeWhitelistRegexFolderName'];
    });

    container.querySelector('#ExcludeWhitelistRegexFileNameResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#ExcludeWhitelistRegexFileName').value =
            configuration['DefaultExcludeWhitelistRegexFileName'];
    });

    container.querySelector('#RegexToolGenerateBtn').addEventListener('click', function (e) {
        e.preventDefault();
        if (!buildRegexToolOutputs()) {
            Dashboard.alert('请先输入路径');
        }
    });

    container.querySelector('#RegexToolCopyPatternBtn').addEventListener('click', function (e) {
        e.preventDefault();
        if (buildRegexToolOutputs()) {
            copyText(container.querySelector('#RegexToolPatternOutput').value, '正则已复制');
        }
    });

    container.querySelector('#RegexToolTestBtn').addEventListener('click', function (e) {
        e.preventDefault();
        runRegexToolTest();
    });

    container.querySelectorAll('.bangumi-tab-container').forEach((tabContainer) => {
        tabContainer.querySelectorAll('.bangumi-tab-header-button').forEach((btn) => {
            btn.addEventListener('keydown', function (event) {
                const tabs = Array.from(
                    tabContainer.querySelectorAll('.bangumi-tab-header-button'),
                ) as HTMLButtonElement[];
                const index = tabs.indexOf(btn);
                let target: number;
                if (event.key === 'ArrowRight') target = (index + 1) % tabs.length;
                else if (event.key === 'ArrowLeft') target = (index + tabs.length - 1) % tabs.length;
                else if (event.key === 'Home') target = 0;
                else if (event.key === 'End') target = tabs.length - 1;
                else return;
                event.preventDefault();
                tabs[target].click();
                tabs[target].focus();
            });
            btn.addEventListener('click', function () {
                tabContainer.querySelectorAll('.bangumi-tab-header-button').forEach((b) => {
                    b.classList.remove('active');
                    b.setAttribute('aria-selected', 'false');
                    b.tabIndex = -1;
                });
                tabContainer.querySelectorAll('.bangumi-tab-content').forEach((tc) => tc.classList.remove('active'));

                btn.classList.add('active');
                btn.setAttribute('aria-selected', 'true');
                btn.tabIndex = 0;

                let id = btn.getAttribute('data-tab');
                tabContainer.querySelectorAll('.bangumi-tab-content').forEach((c) => {
                    if (c.getAttribute('data-tab') == id) {
                        c.classList.add('active');
                    }
                });
            });
        });
    });

    container.querySelectorAll('.bangumi-settings-nav-item').forEach(function (button) {
        button.addEventListener('click', function (e) {
            e.preventDefault();
            switchSettingsSection(getResolvedModule(button.getAttribute('data-target')));
        });
    });

    container.querySelector('#bangumi-media-library-select').addEventListener('change', function () {
        mediaLibraryState.startIndex = 0;
        mediaLibraryState.currentDirectory = null;
        loadMediaLibraryItems();
    });

    function scheduleMediaLibrarySearch() {
        window.clearTimeout(mediaLibraryState.searchTimer);
        mediaLibraryState.searchTimer = window.setTimeout(function () {
            mediaLibraryState.startIndex = 0;
            mediaLibraryState.currentDirectory = null;
            loadMediaLibraryItems();
        }, 300);
    }

    const mediaLibrarySearch = container.querySelector('#bangumi-media-library-search');
    mediaLibrarySearch.addEventListener('compositionstart', function () {
        mediaLibraryState.searchComposing = true;
        window.clearTimeout(mediaLibraryState.searchTimer);
    });
    mediaLibrarySearch.addEventListener('compositionend', function () {
        mediaLibraryState.searchComposing = false;
        scheduleMediaLibrarySearch();
    });
    mediaLibrarySearch.addEventListener('input', function (event) {
        if (mediaLibraryState.searchComposing || event.isComposing) return;
        scheduleMediaLibrarySearch();
    });

    container.querySelector('#bangumi-media-library-refresh').addEventListener('click', function () {
        if (!mediaLibraryState.initialized) {
            initializeMediaLibrary();
            return;
        }
        loadMediaLibraryItems();
    });

    container.querySelector('#bangumi-media-library-back').addEventListener('click', function () {
        mediaLibraryState.currentDirectory = null;
        renderMediaLibraryItems();
    });

    container.querySelector('#bangumi-media-library-previous').addEventListener('click', function () {
        mediaLibraryState.startIndex = Math.max(0, mediaLibraryState.startIndex - mediaLibraryState.pageSize);
        loadMediaLibraryItems();
    });

    container.querySelector('#bangumi-media-library-next').addEventListener('click', function () {
        mediaLibraryState.startIndex += mediaLibraryState.pageSize;
        loadMediaLibraryItems();
    });

    return { show: onLoad, hide: onUnload };
}
