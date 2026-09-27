export function pairCount(count: number) {
  const last = count % 10, teen = count % 100;
  return `${count} ${teen >= 11 && teen <= 14 ? "пар" : last === 1 ? "пара" : last >= 2 && last <= 4 ? "пары" : "пар"}`;
}

export function boundedPan(x: number, y: number, zoom: number, image: { width: number; height: number }, viewport: { width: number; height: number }) {
  const limitX = Math.max(0, (image.width * zoom - viewport.width) / 2);
  const limitY = Math.max(0, (image.height * zoom - viewport.height) / 2);
  return { x: limitX ? Math.max(-limitX, Math.min(limitX, x)) : 0, y: limitY ? Math.max(-limitY, Math.min(limitY, y)) : 0 };
}
