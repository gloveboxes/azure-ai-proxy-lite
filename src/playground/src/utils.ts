export function toCamelCase(
  obj?: Record<string, unknown>
): Record<string, unknown> | undefined {
  if (!obj) return obj;

  return Object.keys(obj).reduce((result, key) => {
    const camelCaseKey = key.replace(/_([a-z])/g, (g) => g[1].toUpperCase());
    const value = obj[key];
    if (Array.isArray(value)) {
      result[camelCaseKey] = value.map((item) =>
        typeof item === "object" && item !== null
          ? toCamelCase(item as Record<string, unknown>)
          : item
      );
    } else if (typeof value === "object" && value !== null) {
      result[camelCaseKey] = toCamelCase(value as Record<string, unknown>);
    } else {
      result[camelCaseKey] = value;
    }
    return result;
  }, {} as Record<string, unknown>);
}
