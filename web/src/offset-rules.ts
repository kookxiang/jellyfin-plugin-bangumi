export interface OffsetRule {
    Selector: string;
    Offset: number;
}

export function parseOffsetRules(text: string): OffsetRule[] {
    return text
        .split(/\r?\n/)
        .map((line, index) => {
            const trimmed = line.trim();
            if (!trimmed) return null;
            const separator = trimmed.lastIndexOf('=');
            const selector = trimmed.slice(0, separator).trim();
            const value = trimmed.slice(separator + 1).trim();
            if (!selector || /[/\\]/.test(selector) || !/^[+-]?\d+$/.test(value)) {
                throw new Error(`第 ${index + 1} 行需要填写“文件名通配符=整数偏移量”。`);
            }
            const offset = Number(value);
            if (!Number.isSafeInteger(offset) || offset < -2147483648 || offset > 2147483647) {
                throw new Error(`第 ${index + 1} 行的偏移量超出整数范围。`);
            }
            return { Selector: selector, Offset: offset };
        })
        .filter((rule): rule is OffsetRule => rule !== null);
}

export function selectOffset(fileName: string, defaultOffset: number, rules: OffsetRule[]) {
    for (const rule of rules) {
        const expression = rule.Selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
            .replace(/\\\*/g, '.*')
            .replace(/\\\?/g, '.');
        if (new RegExp(`^${expression}$`, 'i').test(fileName)) return rule;
    }
    return { Offset: defaultOffset, Selector: '' };
}
