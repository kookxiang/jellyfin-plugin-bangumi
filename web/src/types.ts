/** Jellyfin's injected client includes version-specific APIs used by the legacy controller. */
export interface ApiClient {
    getUrl(path: string, params?: Record<string, unknown>): string;
    fetch(options: { url: string; type?: string; data?: Record<string, unknown> }): Promise<Response>;
    [method: string]: any;
}
export interface Dashboard {
    alert(message: string | { title?: string; message: string }): void;
    confirm(message: string, title: string, callback: (confirmed: boolean) => void): void;
    showLoadingMsg(): void;
    hideLoadingMsg(): void;
    [method: string]: any;
}
export interface Services {
    api: ApiClient;
    dashboard: Dashboard;
}
declare global {
    interface Window {
        ApiClient: ApiClient;
        Dashboard: Dashboard;
    }
}
