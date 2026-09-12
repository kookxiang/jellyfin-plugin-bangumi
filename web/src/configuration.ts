/** Merge only editable, known fields; preserve host/plugin extension settings. */
export function collectConfiguration(
    configuration: Record<string, unknown>,
    controls: Iterable<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>,
) {
    const result = { ...configuration };
    const initializedArrays = new Set<string>();
    for (const control of controls) {
        if (control.hasAttribute('data-config-ignore')) continue;
        if (control.type === 'checkbox' && Array.isArray(configuration[control.name])) {
            if (!initializedArrays.has(control.name)) {
                result[control.name] = [];
                initializedArrays.add(control.name);
            }
            if ((control as HTMLInputElement).checked) (result[control.name] as string[]).push(control.value);
            continue;
        }
        if (!(control.id in configuration)) continue;
        result[control.id] =
            control.type === 'checkbox'
                ? (control as HTMLInputElement).checked
                : typeof configuration[control.id] === 'number'
                  ? Number(control.value)
                  : control.value;
    }
    return result;
}
