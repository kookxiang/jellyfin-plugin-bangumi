import { ToolController, registerTool, action } from './tool.ts';
interface FixResult {
    TotalCount: number;
    RemovedCount: number;
    ValidCount: number;
    NoIdCount: number;
}
export class FixMetadata extends ToolController {
    mount() {
        this.render(
            `<p>移除剧集中为 0 的 Bangumi ID，保留有效 ID。适用于早期版本产生的无效数据。</p>${action('开始修正', 'run')}`,
        );
        this.query('#run').onclick = () =>
            this.run(async () => {
                const result = await this.request<FixResult>('FixEpisodeMetadata/Run', {});
                this.status(
                    `处理完成：共 ${result.TotalCount} 集，移除 ${result.RemovedCount} 个无效 ID，有效 ID ${result.ValidCount} 集，无 ID ${result.NoIdCount} 集。`,
                );
            });
    }
}
registerTool('bangumi-tool-fix-metadata', FixMetadata);
