export function getGalleryIndex(current: number, total: number, direction: -1 | 1): number {
  if (total <= 0) return 0;
  return (current + direction + total) % total;
}

export function clampGalleryZoom(value: number): number {
  return Math.min(2.5, Math.max(1, Number(value.toFixed(2))));
}
