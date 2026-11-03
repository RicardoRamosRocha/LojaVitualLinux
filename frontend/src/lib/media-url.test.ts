import assert from "node:assert/strict";
import test from "node:test";
// @ts-expect-error Node's native TypeScript runner requires the explicit extension.
import { resolveMediaUrl } from "./media-url.ts";

test("preserves HTTPS Cloudinary URLs", () => {
  const url = "https://res.cloudinary.com/vnrcuol7/image/upload/v1/angel-acessorios/products/1/photo.jpg";
  assert.equal(resolveMediaUrl(url), url);
});

test("routes legacy local media through the Next proxy", () => {
  assert.equal(resolveMediaUrl("/images/products/photo.jpg"), "/media/images/products/photo.jpg");
  assert.equal(resolveMediaUrl("videos/products/demo.mp4"), "/media/videos/products/demo.mp4");
});

test("rejects insecure, foreign, and credential-bearing URLs", () => {
  assert.equal(resolveMediaUrl("http://res.cloudinary.com/vnrcuol7/image/upload/photo.jpg"), null);
  assert.equal(resolveMediaUrl("https://images.example.com/photo.jpg"), null);
  assert.equal(resolveMediaUrl("https://user:secret@res.cloudinary.com/vnrcuol7/image/upload/photo.jpg"), null);
  assert.equal(resolveMediaUrl("javascript:alert(1)"), null);
});
