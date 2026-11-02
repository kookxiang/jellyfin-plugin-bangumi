import test from 'node:test';
import assert from 'node:assert/strict';
import { readEventStream } from '../src/tools/event-stream.ts';

const encode = new TextEncoder();
const frame = (event, data) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;

test('SSE handles UTF-8 and CRLF split at every byte, comments and multiple data lines', async () => {
    const events = [];
    const bytes = encode.encode(
        ': keepalive\r\nevent: progress\r\ndata: {"Name":\r\ndata: "剧集"}\r\n\r\n' + frame('complete', {}),
    );
    let index = 0;
    const response = new Response(
        new ReadableStream({
            pull(controller) {
                if (index < bytes.length) controller.enqueue(bytes.slice(index, ++index));
                else controller.close();
            },
        }),
    );
    await readEventStream(response, (...args) => events.push(args), new AbortController().signal);
    assert.deepEqual(events, [
        ['progress', { Name: '剧集' }],
        ['complete', {}],
    ]);
});

test('progress is delivered before the stream finishes; cancellation closes the reader', async () => {
    const abort = new AbortController();
    let canceled = false;
    let received = false;
    const response = new Response(
        new ReadableStream({
            start(controller) {
                controller.enqueue(encode.encode(frame('progress', { ScannedCount: 1 })));
            },
            cancel() {
                canceled = true;
            },
        }),
    );
    await assert.rejects(
        readEventStream(
            response,
            () => {
                received = true;
                abort.abort();
            },
            abort.signal,
        ),
        { name: 'AbortError' },
    );
    assert.equal(received, true);
    assert.equal(canceled, true);
});

test('EOF without completion retains received results and reports interruption', async () => {
    const events = [];
    await assert.rejects(
        readEventStream(
            new Response(frame('progress', { Id: 1 })),
            (...args) => events.push(args),
            new AbortController().signal,
        ),
        /扫描连接中断/,
    );
    assert.deepEqual(events, [['progress', { Id: 1 }]]);
});

test('server error and malformed JSON cancel the stream and propagate', async () => {
    await assert.rejects(
        readEventStream(
            new Response(frame('error', { Message: '查询失败' })),
            (_event, data) => {
                throw new Error(data.Message);
            },
            new AbortController().signal,
        ),
        /查询失败/,
    );
    await assert.rejects(
        readEventStream(new Response('data: invalid\n\n'), () => {}, new AbortController().signal),
        SyntaxError,
    );
});

test('explicit completion stops reading an otherwise open stream', async () => {
    let canceled = false;
    const response = new Response(
        new ReadableStream({
            start(controller) {
                controller.enqueue(encode.encode(frame('complete', {})));
            },
            cancel() {
                canceled = true;
            },
        }),
    );
    await readEventStream(response, () => {}, new AbortController().signal);
    assert.equal(canceled, true);
});
