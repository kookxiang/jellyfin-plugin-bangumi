import template from './account.html?raw';
import styles from './account.css?raw';
import icons from '../host-icons.css?raw';
import './button.ts';
import type { Services } from '../types.ts';

/** Owns account selection and authorization independently of the settings form. */
export class BangumiOAuthContainer extends HTMLElement {
    #controller: ReturnType<typeof createAccountController>;

    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
    }

    configure(services: Services) {
        this.#controller?.hide();
        this.shadowRoot.innerHTML = `<style>${styles}\n${icons}</style>${template}`;
        this.#controller = createAccountController(this.shadowRoot, services);
    }

    show() {
        return this.#controller?.show() ?? Promise.resolve();
    }
    hide() {
        this.#controller?.hide();
    }
    disconnectedCallback() {
        this.hide();
    }
}

function createAccountController(container: any, services: Services) {
    const { api: ApiClient, dashboard: Dashboard } = services;
    let active = false;
    let generation = 0;
    let oauthUsers: { id: string; name: string }[] = [];
    let selectedBangumiUserName = '';

    function wrapLoading(promise: Promise<unknown>) {
        Dashboard.showLoadingMsg();
        return promise
            .catch((error) => {
                if (active && error.name !== 'AbortError') Dashboard.alert('操作失败：' + (error.message || error));
            })
            .finally(() => Dashboard.hideLoadingMsg());
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
        return ApiClient.getUrl(
            '/Plugins/Bangumi/Redirect?prefix=' +
                encodeURIComponent(ApiClient.serverAddress()) +
                '&user=' +
                encodeURIComponent(getSelectedUserId()),
        );
    }

    function copyAuthorizationText(text) {
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

    function getCurrentJellyfinUser() {
        return ApiClient.getCurrentUser().then(function (user) {
            return user ? [user] : [];
        });
    }

    function getJellyfinUsers() {
        return ApiClient.getUsers()
            .then(
                function (users) {
                    return users && users.length ? users : getCurrentJellyfinUser();
                },
                function (error) {
                    console.warn('[Bangumi] Failed to load Jellyfin users, falling back to the current user.', error);
                    return getCurrentJellyfinUser();
                },
            )
            .then(function (users) {
                return users
                    .map(function (user) {
                        return {
                            id: user.Id || user.id || '',
                            name: user.Name || user.name || user.Username || '',
                        };
                    })
                    .filter(function (user) {
                        return user.id && user.name;
                    })
                    .sort(function (left, right) {
                        return left.name.localeCompare(right.name);
                    });
            });
    }

    function loadOAuthUsers() {
        const version = generation;
        var userIdInput = container.querySelector('#bangumi-jellyfin-user');
        var previousUserId = normalizeUserId(userIdInput.value || ApiClient.getCurrentUserId());
        return getJellyfinUsers().then(function (users) {
            if (!active || version !== generation) return;
            oauthUsers = users;
            var selectedUser = users.find(function (user) {
                return normalizeUserId(user.id) === previousUserId;
            });
            userIdInput.value = selectedUser ? selectedUser.id : users[0] ? users[0].id : '';
            renderOAuthUserMenu();
            updateSelectedJellyfinUser();
            return loadOAuthState();
        });
    }

    function renderOAuthUserMenu(query = '') {
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
            check.textContent = 'check';

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
        if (!e.composedPath().includes(selector)) setOAuthUserMenuOpen(false);
    }

    function setJellyfinUserAvatar(avatar, userId) {
        avatar.innerHTML = '<span class="material-icons person">person</span>';
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

    let stateRequest = 0;
    function updateRenewalAction(canRenew: boolean) {
        container.querySelector('#bangumi-oauth-refresh').style.display = canRenew ? '' : 'none';
        container
            .querySelector('#bangumi-oauth-btn')
            .closest('bangumi-button')
            .setAttribute('variant', canRenew ? 'secondary' : 'primary');
    }

    function loadOAuthState() {
        const request = ++stateRequest;
        if (!getSelectedUserId()) return Promise.resolve();
        return ApiClient.getJSON(ApiClient.getUrl(getOAuthRequestPath('/Plugins/Bangumi/OAuthState'))).then(
            function (data) {
                if (!active || request !== stateRequest) return;
                var userInfo = container.querySelector('.bangumi-user-info');
                var avatar = container.querySelector('.bangumi-user-info .user-avatar');
                var dates = container.querySelector('.bangumi-oauth-dates');
                selectedBangumiUserName = '';
                userInfo.dataset.state = !data ? 'unbound' : data.expired ? 'expired' : 'bound';
                container.querySelector('.bangumi-auth-status').textContent = !data
                    ? '未授权'
                    : data.expired
                      ? '授权已过期'
                      : '已授权';
                const hint = container.querySelector('.bangumi-account-hint');
                hint.hidden = !data?.expired;
                hint.textContent = data?.expired ? '请重新授权以恢复播放状态同步。' : '';
                if (!data) {
                    updateOAuthAction(false);
                    container.querySelector('#bangumi-oauth-btn').style.display = '';
                    container.querySelector('#bangumi-oauth-manual-btn').style.display = '';
                    updateRenewalAction(false);
                    container.querySelector('#bangumi-oauth-delete').style.display = 'none';
                    avatar.innerHTML = '<span class="material-icons person">person</span>';
                    container.querySelector('.bangumi-user-info .user-name').textContent = '尚未绑定';
                    dates.style.display = 'none';
                    userInfo.classList.remove('expired');
                    return;
                }
                var bangumiUserName = data.nickname || 'Bangumi 用户';
                selectedBangumiUserName = bangumiUserName;
                updateOAuthAction(true);
                container.querySelector('#bangumi-oauth-btn').style.display = '';
                container.querySelector('#bangumi-oauth-manual-btn').style.display = 'none';
                updateRenewalAction(!!data.autoRefresh && data.expired !== true);
                container.querySelector('#bangumi-oauth-delete').style.display = '';
                avatar.replaceChildren();
                if (!data.avatar) avatar.innerHTML = '<span class="material-icons">person</span>';
                if (data.avatar) {
                    const image = document.createElement('img');
                    image.alt = '';
                    image.src = data.avatar;
                    avatar.append(image);
                }
                container.querySelector('.bangumi-user-info .user-name').textContent = bangumiUserName;
                dates.style.display = 'flex';
                container.querySelector('#bangumi-oauth-effective').textContent = formatOAuthDate(data.effective);
                container.querySelector('#bangumi-oauth-expire').textContent = formatOAuthDate(data.expire);
                userInfo.classList.toggle('expired', data.expired === true);
            },
        );
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

        copyAuthorizationText(authorizationUrl).then(
            function () {
                Dashboard.alert('已复制 ' + getSelectedUserName() + ' 的 Bangumi 授权链接');
            },
            function () {
                Dashboard.alert({ title: '复制失败', message: '请检查浏览器的剪贴板权限。' });
            },
        );
    });

    container.querySelector('#bangumi-oauth-manual-btn').addEventListener('click', function (e) {
        e.preventDefault();
        Dashboard.confirm(
            '<div style="text-align: left"><p>仅在自动授权无法工作时推荐，步骤如下</p><ol><li>打开 <a href="https://next.bgm.tv/demo/access-token/create" style="color: inherit">Access Token 生成页面</a></li><li>创建一个 Token 并复制</li><li>点击确定后填写 Token</li></ol><p style="margin-top: 16px">注：此授权方式无法自动续期，建议选择较长有效期</p></div>',
            '手动授权',
            function (continued) {
                if (!continued) return;
                const token = prompt('请填写 Access Token');
                if (!token) return;
                wrapLoading(
                    ApiClient.fetch({
                        url: getOAuthRequestPath('/Plugins/Bangumi/AccessToken'),
                        type: 'PATCH',
                        data: { token: token },
                    })
                        .then(function () {
                            Dashboard.alert('授权成功');
                            return loadOAuthState();
                        })
                        .catch(function () {
                            Dashboard.alert('授权失败，请检查 Token 是否正确');
                        }),
                );
            },
        );
    });

    container.querySelector('#bangumi-oauth-delete').addEventListener('click', function (e) {
        e.preventDefault();
        var message =
            '确定解除 Jellyfin 用户“' +
            getSelectedUserName() +
            '”与 Bangumi 用户“' +
            selectedBangumiUserName +
            '”的绑定吗？解除后将不再同步该用户的播放进度。';
        Dashboard.confirm(message, '解除绑定', function (confirmed) {
            if (!confirmed) return;
            ApiClient.fetch({ url: getOAuthRequestPath('/Plugins/Bangumi/OAuth'), type: 'DELETE' }).then(function () {
                wrapLoading(loadOAuthState());
            });
        });
    });

    container.querySelector('#bangumi-oauth-refresh').addEventListener('click', function (e) {
        e.preventDefault();
        wrapLoading(
            ApiClient.fetch({
                url: getOAuthRequestPath('/Plugins/Bangumi/RefreshOAuthToken'),
                type: 'POST',
            }).then(
                function () {
                    loadOAuthState();
                    Dashboard.alert('授权有效期已更新');
                },
                function () {
                    Dashboard.alert({ title: '错误', message: '续期失败，请尝试重新授权' });
                    container.querySelector('#bangumi-oauth-btn').style.display = '';
                    updateRenewalAction(false);
                },
            ),
        );
    });

    return {
        show() {
            if (active) return Promise.resolve();
            active = true;
            window.addEventListener('message', windowMessageHandler);
            document.addEventListener('click', documentClickHandler);
            return loadOAuthUsers();
        },
        hide() {
            active = false;
            generation++;
            stateRequest++;
            setOAuthUserMenuOpen(false);
            window.removeEventListener('message', windowMessageHandler);
            document.removeEventListener('click', documentClickHandler);
        },
    };
}

if (!customElements.get('bangumi-oauth-container')) {
    customElements.define('bangumi-oauth-container', BangumiOAuthContainer);
}
