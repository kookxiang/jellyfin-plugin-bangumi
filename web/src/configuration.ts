/** Merge only editable, known fields; preserve host/plugin extension settings. */
export function collectConfiguration(
    configuration: Record<string, unknown>,
    controls: Iterable<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>,
) {
    const result = { ...configuration };
    for (const control of controls) {
        if (control.hasAttribute('data-config-ignore') || !(control.id in configuration)) continue;
        result[control.id] =
            control.type === 'checkbox'
                ? (control as HTMLInputElement).checked
                : typeof configuration[control.id] === 'number'
                  ? Number(control.value)
                  : control.value;
    }
    return result;
}
