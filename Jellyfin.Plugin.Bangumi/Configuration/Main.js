(function () {
    var pluginId = "41b59f1b-a6cf-474a-b416-785379cbd856";
    var container = document.querySelector('#bangumiConfigurationPage:not(.hide)');
    var configuration = {};

    function windowMessageHandler(e) {
        if (e.data === 'BANGUMI-OAUTH-COMPLETE') {
            wrapLoading(loadOAuthState());
        }
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
        return ApiClient.getJSON(ApiClient.getUrl('/Plugins/Bangumi/OAuthState')).then(function (data) {
            if (!data) {
                container.querySelector('#bangumi-oauth-btn').textContent = '授权登录 Bangumi';
                container.querySelector('#bangumi-oauth-btn').style.display = '';
                container.querySelector('#bangumi-oauth-manual-btn').style.display = '';
                container.querySelector('#bangumi-oauth-refresh').style.display = 'none';
                container.querySelector('#bangumi-oauth-delete').style.display = 'none';
                container.querySelector('.bangumi-user-info .user-avatar').innerHTML = '<span class="material-icons person"></span>';
                container.querySelector('.bangumi-user-info .user-name').textContent = '未登录';
                container.querySelector('.bangumi-oauth-status').innerHTML = '';
                return;
            }
            container.querySelector('#bangumi-oauth-btn').textContent = '重新授权';
            container.querySelector('#bangumi-oauth-btn').style.display = 'none';
            container.querySelector('#bangumi-oauth-manual-btn').style.display = 'none';
            container.querySelector('#bangumi-oauth-refresh').style.display = data.autoRefresh ? '' : 'none';
            container.querySelector('#bangumi-oauth-delete').style.display = '';
            container.querySelector('.bangumi-user-info .user-avatar').innerHTML = '<img src="' + data.avatar + '" />';
            container.querySelector('.bangumi-user-info .user-name').textContent = data.nickname;
            container.querySelector('.bangumi-user-info .profile-link').href = data.url;
            container.querySelector('.bangumi-oauth-status').innerHTML = '<p><span class="material-icons schedule"></span> 授权时间: ' + new Date(data.effective).toLocaleString() + '</p><p><span class="material-icons more_time"></span> 过期时间: ' + new Date(data.expire).toLocaleString() + '</p>';
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
                }
            });

            if (container.querySelector('#EpisodeParser')) {
                updateEpisodeParserDisplay();
            }
        });
    }

    function saveConfiguration() {
        var config = Object.assign({}, configuration);
        var elements = container.querySelectorAll('input,select');
        for (var i = 0; i < elements.length; i++) {
            var element = elements[i];
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
        wrapLoading(Promise.all([loadConfiguration(), loadArchiveState(), loadOAuthState(),]));
    }

    function onUnload() {
        window.removeEventListener("message", windowMessageHandler);
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

    container.querySelector('#bangumi-oauth-btn').addEventListener('click', function (e) {
        e.preventDefault();
        window.open(ApiClient.getUrl('/Plugins/Bangumi/Redirect?prefix=' + encodeURIComponent(ApiClient.serverAddress()) + '&user=' + ApiClient.getCurrentUserId()));
    });

    container.querySelector('#bangumi-oauth-manual-btn').addEventListener('click', function (e) {
        e.preventDefault();
        Dashboard.confirm('<div style="text-align: left"><p>仅在自动授权无法工作时推荐，步骤如下</p><ol><li>打开 <a href="https://next.bgm.tv/demo/access-token/create" style="color: inherit">Access Token 生成页面</a></li><li>创建一个 Token 并复制</li><li>点击确定后填写 Token</li></ol><p style="margin-top: 16px">注：此授权方式无法自动续期，建议选择较长有效期</p></div>', '手动授权', function (continued) {
            if (!continued) return;
            const token = prompt('请填写 Access Token');
            if (!token) return;
            wrapLoading(
                ApiClient.fetch({url: '/Plugins/Bangumi/AccessToken', type: 'PATCH', data: {token: token}})
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
        Dashboard.confirm('取消后将断开与 Bangumi 的连接，不再同步播放进度', '取消授权', function (confirmed) {
            if (!confirmed) return;
            ApiClient.fetch({url: '/Plugins/Bangumi/OAuth', type: 'DELETE'})
                .then(function () {
                    wrapLoading(loadOAuthState());
                });
        });
    });

    container.querySelector('#bangumi-oauth-refresh').addEventListener('click', function (e) {
        e.preventDefault();
        wrapLoading(ApiClient.fetch({
            url: '/Plugins/Bangumi/RefreshOAuthToken', type: 'POST'
        })
            .then(function () {
                loadOAuthState();
                Dashboard.alert('授权有效期已更新');
            }, function () {
                Dashboard.alert({title: '错误', message: '续期失败，请尝试重新授权'});
                container.querySelector('#bangumi-oauth-btn').style.display = '';
                container.querySelector('#bangumi-oauth-refresh').style.display = 'none';
            }));
    });

    container.querySelector('#delete-archive-data').addEventListener('click', function (e) {
        e.preventDefault();
        Dashboard.confirm('确定要清空离线数据库吗？', '警告', function (confirmed) {
            if (!confirmed) return;
            Dashboard.showLoadingMsg();
            wrapLoading(ApiClient.fetch({url: '/Plugins/Bangumi/Archive/Store', type: 'DELETE'})
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

    function updateEpisodeParserDisplay() {
        const parser = container.querySelector('#EpisodeParser').value;
        container.querySelectorAll('.episode-parser-options').forEach(el => {
            el.style.display = el.getAttribute('episode-parser') === parser ? '' : 'none';
        });
    }
})();
