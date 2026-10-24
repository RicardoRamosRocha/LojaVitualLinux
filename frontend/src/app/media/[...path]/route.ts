const backendUrl = (process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5268").replace(/\/$/, "");
const allowedMediaPath = /^(images|videos)\/products\/[A-Za-z0-9][A-Za-z0-9._-]*\.(?:jpe?g|png|webp|gif|svg|mp4|webm)$/i;

type MediaRouteContext = {
  params: Promise<{ path: string[] }>;
};

export async function GET(request: Request, context: MediaRouteContext) {
  const { path } = await context.params;
  const relativePath = path.join("/");

  if (!allowedMediaPath.test(relativePath)) {
    return new Response(null, { status: 404 });
  }

  const upstream = await fetch(`${backendUrl}/${relativePath}`, {
    cache: "no-store",
    signal: request.signal,
  });

  if (!upstream.ok || !upstream.body) {
    return new Response(null, { status: upstream.status || 404 });
  }

  const contentType = upstream.headers.get("content-type");
  const isVideo = relativePath.startsWith("videos/");
  const expectedType = isVideo ? "video/" : "image/";
  if (!contentType?.toLowerCase().startsWith(expectedType)) {
    return new Response(null, { status: 415 });
  }

  return new Response(upstream.body, {
    status: 200,
    headers: {
      "Content-Type": contentType,
      "Cache-Control": "public, max-age=300",
    },
  });
}
