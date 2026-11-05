import assert from "node:assert/strict";
import test from "node:test";
// @ts-expect-error Node's native TypeScript runner requires the explicit extension.
import { validateCheckout } from "./checkout-validation.ts";

const customer = {
  customerName: "Ana Silva",
  email: "ana@example.com",
  phone: "11999999999",
};

test("requires the complete delivery address", () => {
  const errors = validateCheckout({
    ...customer,
    zipCode: "01310-100",
    address: "Avenida Paulista",
    number: "1000",
    neighborhood: "Bela Vista",
    city: "São Paulo",
    state: "SP",
    requiresAddress: true,
  });
  assert.deepEqual(errors, {});
});

test("rejects an invalid delivery address", () => {
  const errors = validateCheckout({
    ...customer,
    zipCode: "123",
    address: "",
    number: "",
    neighborhood: "",
    city: "",
    state: "S",
    requiresAddress: true,
  });
  assert.deepEqual(Object.keys(errors).sort(), ["address", "cep", "city", "neighborhood", "number", "state"]);
});

test("does not require delivery fields for pickup", () => {
  assert.deepEqual(validateCheckout({
    ...customer,
    zipCode: "",
    address: "",
    number: "",
    neighborhood: "",
    city: "",
    state: "",
    requiresAddress: false,
  }), {});
});
