/** Consume one request-scoped SSE scan; an interrupted stream is never retried. */
export async function readEventStream(
    response: Response,
    receive: (event: string, data: unknown) => void,
    signal: AbortSignal,
) {
    if (!response.body) throw new Error('无法读取扫描事件流。');
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    const cancel = () => void reader.cancel().catch(() => {});
    signal.addEventListener('abort', cancel, { once: true });
    let buffer = '';
    let event = '';
    let data: string[] = [];
    let complete = false;
    const line = (value: string) => {
        if (!value) {
            if (data.length) {
                receive(event || 'message', JSON.parse(data.join('\n')));
                if (event === 'complete') complete = true;
            }
            event = '';
            data = [];
            return;
        }
        const colon = value.indexOf(':');
        const field = colon < 0 ? value : value.slice(0, colon);
        let content = colon < 0 ? '' : value.slice(colon + 1);
        if (content.startsWith(' ')) content = content.slice(1);
        if (field === 'event') event = content;
        if (field === 'data') data.push(content);
    };
    try {
        signal.throwIfAborted();
        while (!complete) {
            const { value, done } = await reader.read();
            signal.throwIfAborted();
            buffer += decoder.decode(value, { stream: !done });
            let end: number;
            while ((end = buffer.search(/[\r\n]/)) >= 0) {
                // A CRLF pair can be split across network chunks.
                if (buffer[end] === '\r' && end === buffer.length - 1 && !done) break;
                const length = buffer[end] === '\r' && buffer[end + 1] === '\n' ? 2 : 1;
                line(buffer.slice(0, end));
                buffer = buffer.slice(end + length);
                if (complete) break;
            }
            if (done) break;
        }
        if (!complete) throw new Error('扫描连接中断，请重新扫描。');
    } finally {
        signal.removeEventListener('abort', cancel);
        await reader.cancel().catch(() => {});
        reader.releaseLock();
    }
}
