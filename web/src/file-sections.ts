export interface FileSection {
    Selector: string;
    Id?: number;
    Offset?: number;
    Report?: boolean;
    Skip?: boolean;
    CorrectIndex?: boolean;
    Type?: 'Auto' | 'Normal' | 'Special';
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
