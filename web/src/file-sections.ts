export interface FileSection {
    Selector: string;
    Id?: number;
    Offset?: number;
    Report?: boolean;
    Skip?: boolean;
    CorrectIndex?: boolean;
    Type?: 'Auto' | 'Normal' | 'Special';
}

const integerFields = new Set(['Id', 'Offset']);
const booleanFields = new Set(['Report', 'Skip', 'CorrectIndex']);

export function parseSections(text: string): FileSection[] {
    const sections: FileSection[] = [];
    let current: FileSection | null = null;
    for (const [index, source] of text.split(/\r?\n/).entries()) {
        const line = source.trim();
        if (!line || line.startsWith('#') || line.startsWith(';')) continue;
        if (line.startsWith('[') && line.endsWith(']')) {
            if (!/^\[Section\.[^\]]+\]$/i.test(line)) throw new Error(`第 ${index + 1} 行需要填写 [Section.编号]。`);
            current = { Selector: '' };
            sections.push(current);
            continue;
        }
        if (!current) throw new Error(`第 ${index + 1} 行之前需要 [Section.编号]。`);
        const separator = line.indexOf('=');
        if (separator < 0) throw new Error(`第 ${index + 1} 行需要填写“字段=值”。`);
        const key = line.slice(0, separator).trim().toLowerCase();
        const value = line.slice(separator + 1).trim();
        const name =
            key === 'id' ? 'Id' : key === 'correctindex' ? 'CorrectIndex' : key.charAt(0).toUpperCase() + key.slice(1);
        if (name === 'Selector') {
            if (!value || /[/\\\r\n]/.test(value)) throw new Error(`第 ${index + 1} 行的文件名选择器无效。`);
            current.Selector = value;
        } else if (integerFields.has(name)) {
            if (
                !/^[+-]?\d+$/.test(value) ||
                !Number.isSafeInteger(Number(value)) ||
                Number(value) < -2147483648 ||
                Number(value) > 2147483647 ||
                (name === 'Id' && Number(value) < 0)
            )
                throw new Error(`第 ${index + 1} 行的 ${name} 需要是有效整数。`);
            current[name] = Number(value);
        } else if (booleanFields.has(name)) {
            if (/^(on|yes|true|1)$/i.test(value)) current[name] = true;
            else if (/^(off|no|false|0)$/i.test(value)) current[name] = false;
            else throw new Error(`第 ${index + 1} 行的 ${name} 需要填写 on 或 off。`);
        } else if (name === 'Type') {
            const type = ['Auto', 'Normal', 'Special'].find((item) => item.toLowerCase() === value.toLowerCase());
            if (!type) throw new Error(`第 ${index + 1} 行的 Type 无效。`);
            current.Type = type as FileSection['Type'];
        } else {
            throw new Error(`第 ${index + 1} 行的字段 ${name} 不受支持。`);
        }
    }
    if (sections.some((section) => !section.Selector)) throw new Error('每个 [Section.编号] 都需要 Selector。');
    return sections;
}

export function formatSections(sections: FileSection[]): string {
    return sections
        .map((section, index) => {
            const lines = [`[Section.${index + 1}]`, `Selector=${section.Selector}`];
            for (const key of ['Id', 'Offset', 'Report', 'Skip', 'CorrectIndex', 'Type'] as const) {
                const value = section[key];
                if (value === undefined || value === null) continue;
                lines.push(
                    `${key === 'Id' ? 'ID' : key}=${typeof value === 'boolean' ? (value ? 'on' : 'off') : value}`,
                );
            }
            return lines.join('\n');
        })
        .join('\n\n');
}

export function selectSection(fileName: string, sections: FileSection[]): FileSection | null {
    for (const section of sections) {
        const expression = section.Selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
            .replace(/\\\*/g, '.*')
            .replace(/\\\?/g, '.');
        if (new RegExp(`^${expression}$`, 'i').test(fileName)) return section;
    }
    return null;
}
