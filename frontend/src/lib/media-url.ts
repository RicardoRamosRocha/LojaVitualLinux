const mediaPathPattern = /^(?:images|videos)\/products\/[A-Za-z0-9][A-Za-z0-9._-]*\.(?:jpe?g|png|webp|gif|svg|mp4|webm)$/i;

const allowedCloudinaryHost = "res.cloudinary.com";

export function resolveMediaUrl(value: string | null | undefined): string | null {
  if (!value) return null;

  const trimmedValue = value.trim();
  if (!trimmedValue) return null;

  try {
    const url = new URL(trimmedValue);
    if (
      url.protocol === "https:" &&
      url.hostname === allowedCloudinaryHost &&
      !url.username &&
      !url.password &&
      !url.port
    ) {
      return url.toString();
    }

    return null;
  } catch {
    const normalizedPath = trimmedValue.replace(/^\/+/, "");
    return mediaPathPattern.test(normalizedPath) ? `/media/${normalizedPath}` : null;
  }
}
