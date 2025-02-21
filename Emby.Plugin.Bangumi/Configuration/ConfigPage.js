define(['baseView', 'emby-scroller', 'emby-select', 'emby-input', 'emby-checkbox', 'emby-button'], function (BaseView) {
    'use strict';

    function View() {
        BaseView.apply(this, arguments);
        var pluginId = "41b59f1b-a6cf-474a-b416-785379cbd856";
        var container = document.querySelector('#bangumiConfigurationPage:not(.hide)');
        var configuration = {};

        function windowMessageHandler(e) {
            if (e.data === 'BANGUMI-OAUTH-COMPLETE') {
                wrapLoading(loadOAuthState());
            }
        }

        function loadOAuthState() {
            return ApiClient.getJSON(ApiClient.getUrl('/Bangumi/OAuthState')).then(function (data) {
                if (!data) {
                    container.querySelector('#bangumi-oauth-btn').textContent = '授权登录 Bangumi';
                    container.querySelector('#bangumi-oauth-btn').style.display = '';
                    container.querySelector('#bangumi-oauth-refresh').style.display = 'none';
                    container.querySelector('#bangumi-oauth-delete').style.display = 'none';
                    container.querySelector('.bangumi-user-info .user-avatar').innerHTML = '<span class="material-icons person"></span>';
                    container.querySelector('.bangumi-user-info .user-name').textContent = '未登录';
                    container.querySelector('.bangumi-oauth-status').innerHTML = '';
                    return;
                }
                container.querySelector('#bangumi-oauth-btn').textContent = '重新授权';
                container.querySelector('#bangumi-oauth-btn').style.display = 'none';
                container.querySelector('#bangumi-oauth-refresh').style.display = '';
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
            wrapLoading(Promise.all([loadConfiguration(), loadOAuthState(),]));
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

        container.querySelector('#bangumiConfigurationForm').addEventListener('change', saveConfiguration);

        container.querySelector('#bangumi-oauth-btn').addEventListener('click', function (e) {
            e.preventDefault();
            window.open(ApiClient.getUrl('/Bangumi/Redirect?prefix=' + encodeURIComponent(ApiClient.serverAddress()) + '&user=' + ApiClient.getCurrentUserId()));
        });

        container.querySelector('#bangumi-oauth-delete').addEventListener('click', function (e) {
            e.preventDefault();
            Dashboard.confirm('取消后将断开与 Bangumi 的连接，不再同步播放进度', '取消授权', function (confirmed) {
                if (!confirmed) return;
                ApiClient.fetch({url: '/Bangumi/OAuth', type: 'DELETE'})
                    .then(function () {
                        wrapLoading(loadOAuthState());
                    });
            });
        });

        container.querySelector('#bangumi-oauth-refresh').addEventListener('click', function (e) {
            e.preventDefault();
            wrapLoading(ApiClient.fetch({
                url: '/Bangumi/RefreshOAuthToken', type: 'POST'
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
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function (options) {
        BaseView.prototype.onResume.apply(this, arguments);
    };

    return View;
});
