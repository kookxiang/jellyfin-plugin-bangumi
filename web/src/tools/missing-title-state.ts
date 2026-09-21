export interface MissingTitleItem {
    Id: string;
    Name?: string;
    Path?: string;
    BangumiId: string;
    SeriesId?: string;
    SeriesName?: string;
    Reason: string;
}

export function groupMissingTitles(items: MissingTitleItem[]) {
    const groups = new Map<string, { title: string; seriesId?: string; items: MissingTitleItem[] }>();
    for (const item of items) {
        const seriesId =
            item.SeriesId && !/^0{32}$/.test(item.SeriesId.replaceAll('-', '')) ? item.SeriesId : undefined;
        const key = seriesId || item.SeriesName || 'other';
        if (!groups.has(key)) groups.set(key, { title: item.SeriesName || '其他视频', seriesId, items: [] });
        groups.get(key)!.items.push(item);
    }
    return [...groups.values()];
}
