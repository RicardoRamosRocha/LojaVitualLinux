import assert from "node:assert/strict";
import test from "node:test";
// @ts-expect-error Node's native TypeScript runner requires the explicit extension.
import { clampGalleryZoom, getGalleryIndex } from "./gallery-navigation.ts";

test("navega em ciclo entre as imagens", () => {
  assert.equal(getGalleryIndex(0, 4, -1), 3);
  assert.equal(getGalleryIndex(3, 4, 1), 0);
  assert.equal(getGalleryIndex(1, 4, 1), 2);
});

test("trata galeria vazia e limita o zoom", () => {
  assert.equal(getGalleryIndex(0, 0, 1), 0);
  assert.equal(clampGalleryZoom(0.5), 1);
  assert.equal(clampGalleryZoom(3), 2.5);
  assert.equal(clampGalleryZoom(1.25), 1.25);
});
