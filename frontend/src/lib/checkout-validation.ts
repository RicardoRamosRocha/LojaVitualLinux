export type CheckoutValidationInput = {
  customerName: string;
  email: string;
  phone: string;
  zipCode: string;
  address: string;
  number: string;
  neighborhood: string;
  city: string;
  state: string;
  requiresAddress: boolean;
};

export type CheckoutValidationErrors = Partial<Record<string, string>>;

export function validateCheckout(input: CheckoutValidationInput): CheckoutValidationErrors {
  const errors: CheckoutValidationErrors = {};
  if (!input.customerName.trim()) errors.customerName = "Informe seu nome completo.";
  if (!input.email.trim()) errors.email = "Informe seu e-mail.";
  if (!input.phone.trim()) errors.phone = "Informe seu telefone.";

  if (input.requiresAddress) {
    if (!/^\d{5}-?\d{3}$/.test(input.zipCode.trim())) errors.cep = "Informe um CEP válido com 8 dígitos.";
    if (!input.address.trim()) errors.address = "Informe a rua ou avenida.";
    if (!input.number.trim()) errors.number = "Informe o número.";
    if (!input.neighborhood.trim()) errors.neighborhood = "Informe o bairro.";
    if (!input.city.trim()) errors.city = "Informe a cidade.";
    if (!/^[A-Za-zÀ-ÿ]{2}$/.test(input.state.trim())) errors.state = "Informe a UF com 2 letras.";
  }

  return errors;
}
