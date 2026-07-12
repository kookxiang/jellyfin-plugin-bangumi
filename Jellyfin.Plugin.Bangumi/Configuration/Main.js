(function () {
    var pluginId = "41b59f1b-a6cf-474a-b416-785379cbd856";
    var container = document.querySelector('#bangumiConfigurationPage:not(.hide)');
    var configuration = {};
    var oauthUsers = [];
    var selectedBangumiUserName = '';

    function getAvailableModules() {
        return Array.from(container.querySelectorAll('.bangumi-settings-nav-item'))
            .map(function (button) {
                return button.getAttribute('data-target');
            })
            .filter(Boolean);
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
    }

    function windowMessageHandler(e) {
        if (e.data === 'BANGUMI-OAUTH-COMPLETE') {
            wrapLoading(loadOAuthState());
        }
    }

    function normalizeUserId(userId) {
        return (userId || '').replace(/-/g, '').toLowerCase();
    }

    function getSelectedUserId() {
        return container.querySelector('#bangumi-jellyfin-user').value;
    }

    function getSelectedUserName() {
        var selectedUserId = normalizeUserId(getSelectedUserId());
        var selectedUser = oauthUsers.find(function (user) {
            return normalizeUserId(user.id) === selectedUserId;
        });
        return selectedUser ? selectedUser.name : '';
    }

    function getOAuthRequestPath(path) {
        return path + '?userId=' + encodeURIComponent(getSelectedUserId());
    }

    function getAuthorizationUrl() {
        return ApiClient.getUrl('/Plugins/Bangumi/Redirect?prefix='
            + encodeURIComponent(ApiClient.serverAddress())
            + '&user=' + encodeURIComponent(getSelectedUserId()));
    }

    function copyText(text) {
        if (navigator.clipboard && window.isSecureContext) {
            return navigator.clipboard.writeText(text);
        }

        var input = document.createElement('textarea');
        input.value = text;
        input.style.position = 'fixed';
        input.style.opacity = '0';
        document.body.appendChild(input);
        input.select();
        var copied = document.execCommand('copy');
        document.body.removeChild(input);
        return copied ? Promise.resolve() : Promise.reject(new Error('copy failed'));
    }

    function loadOAuthUsers() {
        var userIdInput = container.querySelector('#bangumi-jellyfin-user');
        var previousUserId = normalizeUserId(userIdInput.value || ApiClient.getCurrentUserId());
        return ApiClient.getJSON(ApiClient.getUrl('/Plugins/Bangumi/OAuthUsers')).then(function (users) {
            oauthUsers = users;
            var selectedUser = users.find(function (user) {
                return normalizeUserId(user.id) === previousUserId;
            });
            userIdInput.value = selectedUser ? selectedUser.id : (users[0] ? users[0].id : '');
            renderOAuthUserMenu();
            updateSelectedJellyfinUser();
            return loadOAuthState();
        });
    }

    function renderOAuthUserMenu(query) {
        var list = container.querySelector('#bangumi-jellyfin-user-menu-list');
        var empty = container.querySelector('.bangumi-jellyfin-user-menu-empty');
        var normalizedQuery = (query || '').trim().toLocaleLowerCase();
        var filteredUsers = oauthUsers.filter(function (user) {
            return !normalizedQuery || user.name.toLocaleLowerCase().includes(normalizedQuery);
        });
        list.innerHTML = '';
        filteredUsers.forEach(function (user) {
            var item = document.createElement('button');
            item.className = 'bangumi-jellyfin-user-menu-item';
            item.type = 'button';
            item.setAttribute('role', 'option');
            item.setAttribute('data-user-id', user.id);

            var avatar = document.createElement('span');
            avatar.className = 'bangumi-jellyfin-user-menu-avatar';
            setJellyfinUserAvatar(avatar, user.id);

            var name = document.createElement('span');
            name.className = 'bangumi-jellyfin-user-menu-name';
            name.textContent = user.name;

            var check = document.createElement('span');
            check.className = 'material-icons check bangumi-jellyfin-user-menu-check';

            item.appendChild(avatar);
            item.appendChild(name);
            item.appendChild(check);
            list.appendChild(item);
        });
        empty.hidden = filteredUsers.length > 0;
        updateOAuthUserMenuSelection();
    }

    function updateOAuthUserMenuSelection() {
        var selectedUserId = normalizeUserId(getSelectedUserId());
        container.querySelectorAll('.bangumi-jellyfin-user-menu-item').forEach(function (item) {
            var selected = normalizeUserId(item.getAttribute('data-user-id')) === selectedUserId;
            item.classList.toggle('selected', selected);
            item.setAttribute('aria-selected', selected ? 'true' : 'false');
        });
    }

    function setOAuthUserMenuOpen(open) {
        var button = container.querySelector('#bangumi-jellyfin-user-switch');
        var menu = container.querySelector('#bangumi-jellyfin-user-menu');
        button.setAttribute('aria-expanded', open ? 'true' : 'false');
        menu.hidden = !open;
        if (open) {
            var search = container.querySelector('#bangumi-jellyfin-user-search');
            search.value = '';
            renderOAuthUserMenu();
            search.focus();
        }
    }

    function documentClickHandler(e) {
        var selector = container.querySelector('.bangumi-jellyfin-user-selector');
        if (!selector.contains(e.target)) setOAuthUserMenuOpen(false);
    }

    function setJellyfinUserAvatar(avatar, userId) {
        avatar.innerHTML = '<span class="material-icons person"></span>';
        if (!userId) return;

        var image = document.createElement('img');
        image.alt = '';
        image.onload = function () {
            avatar.innerHTML = '';
            avatar.appendChild(image);
        };
        image.src = ApiClient.getUrl('/Users/' + encodeURIComponent(userId) + '/Images/Primary');
    }

    function updateSelectedJellyfinUser() {
        var avatar = container.querySelector('#bangumi-jellyfin-user-avatar');
        var userId = getSelectedUserId();
        container.querySelector('.bangumi-jellyfin-user-name').textContent = getSelectedUserName() || '—';
        setJellyfinUserAvatar(avatar, userId);
        updateOAuthUserMenuSelection();
    }

    function loadArchiveState() {
        return ApiClient.getJSON(ApiClient.getUrl('/Plugins/Bangumi/Archive/Status')).then(function (data) {
            // size
            var size = data.size || 0;

            if (size > 0) {
                container.querySelector('#bangumi-archive-container').classList.add('has-archive-data');

                var units = ['B', 'KB', 'MB', 'GB', 'TB'];
                var index = Math.floor(Math.log2(size) / 10);
                container.querySelector('#archive-size').textContent = (size / Math.pow(1024, index)).toFixed(2) + ' ' + units[index];
            } else {
                container.querySelector('#bangumi-archive-container').classList.remove('has-archive-data');
                container.querySelector('#archive-folder').textContent = '(不存在)';
                return;
            }

            // path
            container.querySelector('#archive-folder').textContent = data.path;

            // update time
            container.querySelector('#archive-update-time').textContent = data.time ? new Intl.DateTimeFormat('zh-Hans', {
                dateStyle: 'long', timeStyle: 'long'
            }).format(new Date(data.time)) : '-';

            window.ApiClient.getScheduledTasks().then(function (tasks) {
                var task = tasks.find(function (task) {
                    return task.Key === "ArchiveDataDownloadTask" && task.Category === "Bangumi";
                });
                if (!task) return;
                var link = container.querySelector('#archive-update-schedule-link');
                link.href = '#/dashboard/tasks/' + task.Id;
                link.style.display = '';

                var button = container.querySelector('#config-archive-update-task');
                button.style.display = '';
                button.addEventListener('click', function () {
                    link.click();
                });
            });
        });
    }

    function loadOAuthState() {
        if (!getSelectedUserId()) return Promise.resolve();
        return ApiClient.getJSON(ApiClient.getUrl(getOAuthRequestPath('/Plugins/Bangumi/OAuthState'))).then(function (data) {
            var userInfo = container.querySelector('.bangumi-user-info');
            var avatar = container.querySelector('.bangumi-user-info .user-avatar');
            var dates = container.querySelector('.bangumi-oauth-dates');
            selectedBangumiUserName = '';
            if (!data) {
                updateOAuthAction(false);
                container.querySelector('#bangumi-oauth-btn').style.display = '';
                container.querySelector('#bangumi-oauth-manual-btn').style.display = '';
                container.querySelector('#bangumi-oauth-refresh').style.display = 'none';
                container.querySelector('#bangumi-oauth-delete').style.display = 'none';
                avatar.innerHTML = '<span class="material-icons person"></span>';
                container.querySelector('.bangumi-user-info .user-name').textContent = '尚未绑定';
                dates.style.display = 'none';
                userInfo.classList.remove('expired');
                return;
            }
            selectedBangumiUserName = data.nickname || '';
            updateOAuthAction(true);
            container.querySelector('#bangumi-oauth-btn').style.display = '';
            container.querySelector('#bangumi-oauth-manual-btn').style.display = 'none';
            container.querySelector('#bangumi-oauth-refresh').style.display = data.autoRefresh ? '' : 'none';
            container.querySelector('#bangumi-oauth-delete').style.display = '';
            avatar.innerHTML = data.avatar
                ? '<img src="' + data.avatar + '" />'
                : '<span class="material-icons person"></span>';
            container.querySelector('.bangumi-user-info .user-name').textContent = data.nickname;
            dates.style.display = 'flex';
            container.querySelector('#bangumi-oauth-effective').textContent = formatOAuthDate(data.effective);
            container.querySelector('#bangumi-oauth-expire').textContent = formatOAuthDate(data.expire);
            userInfo.classList.toggle('expired', data.expired === true);
        });
    }

    function formatOAuthDate(value) {
        if (!value) return '—';
        var date = new Date(value);
        return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString();
    }

    function updateOAuthAction(hasBinding) {
        var isCurrentUser = normalizeUserId(getSelectedUserId()) === normalizeUserId(ApiClient.getCurrentUserId());
        var button = container.querySelector('#bangumi-oauth-btn');
        if (isCurrentUser) {
            button.textContent = hasBinding ? '重新授权' : '授权登录 Bangumi';
        } else {
            button.textContent = hasBinding ? '复制重新授权链接' : '复制授权链接';
        }
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
                }
            });

            if (container.querySelector('#EpisodeParser')) {
                updateEpisodeParserDisplay();
            }

            updateNSFWReportDisplay();
        });
    }

    function updateNSFWReportDisplay() {
        var skipNSFWReport = container.querySelector('#SkipNSFWPlaybackReport');
        var privateNSFWReportContainer = container.querySelector('#PrivateNSFWPlaybackReportContainer');
        if (!skipNSFWReport || !privateNSFWReportContainer) return;

        privateNSFWReportContainer.style.display = skipNSFWReport.checked ? 'none' : '';
    }

    function saveConfiguration() {
        var config = Object.assign({}, configuration);
        var elements = container.querySelectorAll('input,select,textarea');
        for (var i = 0; i < elements.length; i++) {
            var element = elements[i];

            // 跳过不需要保存的元素
            if (element.hasAttribute('data-config-ignore')) {
                continue;
            }

            if (element.type === 'checkbox') {
                config[element.id] = element.checked;
            } else {
                config[element.id] = element.value;
            }
        }
        return wrapLoading(ApiClient.updatePluginConfiguration(pluginId, config)
            .then(Dashboard.processPluginConfigurationUpdateResult));
    }

    function onLoad() {
        window.addEventListener("message", windowMessageHandler);
        window.addEventListener('hashchange', applyModuleFromHash);
        document.addEventListener('click', documentClickHandler);
        applyModuleFromHash();
        wrapLoading(Promise.all([loadConfiguration(), loadArchiveState(), loadOAuthUsers(),]));
    }

    function onUnload() {
        window.removeEventListener("message", windowMessageHandler);
        window.removeEventListener('hashchange', applyModuleFromHash);
        document.removeEventListener('click', documentClickHandler);
    }

    function wrapLoading(promise) {
        Dashboard.showLoadingMsg();
        promise.then(Dashboard.hideLoadingMsg, Dashboard.hideLoadingMsg);
    }

    container.addEventListener('viewshow', onLoad);
    container.addEventListener('viewhide', onUnload);

    container.querySelector('#bangumiConfigurationForm').addEventListener('submit', function (e) {
        e.preventDefault();
        saveConfiguration();
    });

    container.querySelector('#SkipNSFWPlaybackReport').addEventListener('change', updateNSFWReportDisplay);

    container.querySelector('#bangumi-jellyfin-user-switch').addEventListener('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        var open = this.getAttribute('aria-expanded') !== 'true';
        setOAuthUserMenuOpen(open);
    });

    container.querySelector('#bangumi-jellyfin-user-menu').addEventListener('click', function (e) {
        e.stopPropagation();
        var item = e.target.closest('.bangumi-jellyfin-user-menu-item');
        if (!item) return;
        container.querySelector('#bangumi-jellyfin-user').value = item.getAttribute('data-user-id');
        updateSelectedJellyfinUser();
        setOAuthUserMenuOpen(false);
        wrapLoading(loadOAuthState());
    });

    container.querySelector('#bangumi-jellyfin-user-search').addEventListener('input', function () {
        renderOAuthUserMenu(this.value);
    });

    container.querySelector('#bangumi-jellyfin-user-menu').addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;
        setOAuthUserMenuOpen(false);
        container.querySelector('#bangumi-jellyfin-user-switch').focus();
    });

    container.querySelector('#bangumi-oauth-btn').addEventListener('click', function (e) {
        e.preventDefault();
        var authorizationUrl = getAuthorizationUrl();
        var isCurrentUser = normalizeUserId(getSelectedUserId()) === normalizeUserId(ApiClient.getCurrentUserId());
        if (isCurrentUser) {
            window.open(authorizationUrl);
            return;
        }

        copyText(authorizationUrl).then(function () {
            Dashboard.alert('已复制 ' + getSelectedUserName() + ' 的 Bangumi 授权链接');
        }, function () {
            Dashboard.alert({ title: '复制失败', message: '请检查浏览器的剪贴板权限。' });
        });
    });

    container.querySelector('#bangumi-oauth-manual-btn').addEventListener('click', function (e) {
        e.preventDefault();
        Dashboard.confirm('<div style="text-align: left"><p>仅在自动授权无法工作时推荐，步骤如下</p><ol><li>打开 <a href="https://next.bgm.tv/demo/access-token/create" style="color: inherit">Access Token 生成页面</a></li><li>创建一个 Token 并复制</li><li>点击确定后填写 Token</li></ol><p style="margin-top: 16px">注：此授权方式无法自动续期，建议选择较长有效期</p></div>', '手动授权', function (continued) {
            if (!continued) return;
            const token = prompt('请填写 Access Token');
            if (!token) return;
            wrapLoading(
                ApiClient.fetch({ url: getOAuthRequestPath('/Plugins/Bangumi/AccessToken'), type: 'PATCH', data: { token: token } })
                    .then(function () {
                        Dashboard.alert('授权成功');
                        return loadOAuthState();
                    })
                    .catch(function () {
                        Dashboard.alert('授权失败，请检查 Token 是否正确');
                    })
            );
        });
    });

    container.querySelector('#bangumi-oauth-delete').addEventListener('click', function (e) {
        e.preventDefault();
        var message = '确定解除 Jellyfin 用户“' + getSelectedUserName() + '”与 Bangumi 用户“'
            + selectedBangumiUserName + '”的绑定吗？解除后将不再同步该用户的播放进度。';
        Dashboard.confirm(message, '解除绑定', function (confirmed) {
            if (!confirmed) return;
            ApiClient.fetch({ url: getOAuthRequestPath('/Plugins/Bangumi/OAuth'), type: 'DELETE' })
                .then(function () {
                    wrapLoading(loadOAuthState());
                });
        });
    });

    container.querySelector('#bangumi-oauth-refresh').addEventListener('click', function (e) {
        e.preventDefault();
        wrapLoading(ApiClient.fetch({
            url: getOAuthRequestPath('/Plugins/Bangumi/RefreshOAuthToken'), type: 'POST'
        })
            .then(function () {
                loadOAuthState();
                Dashboard.alert('授权有效期已更新');
            }, function () {
                Dashboard.alert({ title: '错误', message: '续期失败，请尝试重新授权' });
                container.querySelector('#bangumi-oauth-btn').style.display = '';
                container.querySelector('#bangumi-oauth-refresh').style.display = 'none';
            }));
    });

    container.querySelector('#delete-archive-data').addEventListener('click', function (e) {
        e.preventDefault();
        Dashboard.confirm('确定要清空离线数据库吗？', '警告', function (confirmed) {
            if (!confirmed) return;
            Dashboard.showLoadingMsg();
            wrapLoading(ApiClient.fetch({ url: '/Plugins/Bangumi/Archive/Store', type: 'DELETE' })
                .then(function () {
                    loadArchiveState();
                    Dashboard.alert('离线数据库已清空');
                }));
        })
    });

    container.querySelector('#EpisodeParser').addEventListener('change', function (e) {
        e.preventDefault();
        updateEpisodeParserDisplay();
    });

    function switchSettingsSection(target) {
        if (!target) {
            return;
        }

        var resolvedTarget = getResolvedModule(target);

        container.querySelectorAll('.bangumi-settings-nav-item').forEach(function (button) {
            button.classList.toggle('active', button.getAttribute('data-target') === resolvedTarget);
        });

        container.querySelectorAll('.bangumi-settings-panel').forEach(function (panel) {
            panel.classList.toggle('active', panel.getAttribute('data-section') === resolvedTarget);
        });
    }

    function updateEpisodeParserDisplay() {
        const parser = container.querySelector('#EpisodeParser').value;
        container.querySelectorAll('.episode-parser-options').forEach(el => {
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
            .map(l => l.trim())
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
            return navigator.clipboard.writeText(value).then( () => {
                Dashboard.alert(successMessage);
                return true;
            }, () => {
                Dashboard.alert('复制失败，请手动复制');
                return false;
            });
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
        (lines || []).forEach(line => {
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
        (items || []).forEach(item => {
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
                    { label: '完整路径', value: resolved.fullPath, patterns: container.querySelector('#ExcludeWhitelistRegexFullPath').value },
                    { label: '目录名称', value: resolved.folderName, patterns: container.querySelector('#ExcludeWhitelistRegexFolderName').value },
                    { label: '文件名', value: episodeFileName, patterns: container.querySelector('#ExcludeWhitelistRegexFileName').value },
                ],
            },
            {
                title: '特典文件排除',
                inputs: [
                    { label: '完整路径', value: resolved.fullPath, patterns: container.querySelector('#SpExcludeRegexFullPath').value },
                    { label: '目录名称', value: resolved.folderName, patterns: container.querySelector('#SpExcludeRegexFolderName').value },
                    { label: '文件名', value: episodeFileName, patterns: container.querySelector('#SpExcludeRegexFileName').value },
                ],
            },
            {
                title: '杂项文件排除',
                inputs: [
                    { label: '完整路径', value: resolved.fullPath, patterns: container.querySelector('#MiscExcludeRegexFullPath').value },
                    { label: '目录名称', value: resolved.folderName, patterns: container.querySelector('#MiscExcludeRegexFolderName').value },
                    { label: '文件名', value: episodeFileName, patterns: container.querySelector('#MiscExcludeRegexFileName').value },
                ],
            },
        ];

        // 是否命中
        var whitelistMatched = false;
        var specialMatched = false;
        var miscMatched = false;

        // 汇总每个正则组的命中结果和无效正则
        groups.forEach(group => {
            var matchedLine = null;
            var invalidLines = [];
            var lines = [];

            // 逐条测试输入项，记录命中和无效正则
            group.inputs.forEach(item => {
                var testResult = testRegexLines(item.patterns, item.value);

                // 记录无效正则
                invalidLines = invalidLines.concat(testResult.invalid.map(invalidItem => {
                    return item.label + ' 第 ' + invalidItem.lineNumber + ' 行无效: ' + invalidItem.pattern + ' (' + invalidItem.message + ')';
                }));

                // 记录首个命中正则
                if (!matchedLine && testResult.matched) {
                    matchedLine = item.label + ' 第 ' + testResult.matched.lineNumber + ' 行命中: ' + testResult.matched.pattern;
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
                state: matchedLine ? 'hit' : (invalidLines.length ? 'invalid' : 'miss'),
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
        if(matchTypes.length == 0) {
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
        container.querySelector('#MiscExcludeRegexFolderName').value = configuration['DefaultMiscExcludeRegexFolderName'];
    });

    container.querySelector('#MiscExcludeRegexFileNameResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#MiscExcludeRegexFileName').value = configuration['DefaultMiscExcludeRegexFileName'];
    });

    container.querySelector('#ExcludeWhitelistRegexFullPathResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#ExcludeWhitelistRegexFullPath').value = configuration['DefaultExcludeWhitelistRegexFullPath'];
    });

    container.querySelector('#ExcludeWhitelistRegexFolderNameResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#ExcludeWhitelistRegexFolderName').value = configuration['DefaultExcludeWhitelistRegexFolderName'];
    });

    container.querySelector('#ExcludeWhitelistRegexFileNameResetBtn').addEventListener('click', function (e) {
        e.preventDefault();
        container.querySelector('#ExcludeWhitelistRegexFileName').value = configuration['DefaultExcludeWhitelistRegexFileName'];
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

    container.querySelectorAll('.bangumi-tab-container').forEach(tabContainer => {
        tabContainer.querySelectorAll('.bangumi-tab-header-button').forEach(btn => {
            btn.addEventListener('click', function () {
                tabContainer.querySelectorAll('.bangumi-tab-header-button').forEach(b => b.classList.remove('active'));
                tabContainer.querySelectorAll('.bangumi-tab-content').forEach(tc => tc.classList.remove('active'));

                btn.classList.add('active');

                let id = btn.getAttribute('data-tab');
                tabContainer.querySelectorAll('.bangumi-tab-content').forEach(c => {
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
            switchSettingsSection(getResolvedModule(button.getAttribute('data-target')), false);
        });
    });

    container.querySelectorAll('.bangumi-plugin-tools').forEach(function (link) {
        link.addEventListener('click', function (e) {
            e.preventDefault();
            var href = link.getAttribute('href');
            if (!href) {
                return;
            }
            Dashboard.navigate(href.replace(/^#/, ''));
        });
    });
})();
