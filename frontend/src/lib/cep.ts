export type CepAddress = {
  street: string;
  neighborhood: string;
  city: string;
  state: string;
};

export class CepLookupError extends Error {
  readonly kind: "invalid" | "not_found" | "network" | "timeout";

  constructor(kind: CepLookupError["kind"], message: string) {
    super(message);
    this.name = "CepLookupError";
    this.kind = kind;
  }
}

export function normalizeCep(value: string) {
  return value.replace(/\D/g, "").slice(0, 8);
}

export function formatCep(value: string) {
  const digits = normalizeCep(value);
  return digits.length > 5 ? `${digits.slice(0, 5)}-${digits.slice(5)}` : digits;
}

export async function lookupCep(value: string, signal?: AbortSignal): Promise<CepAddress> {
  const cep = normalizeCep(value);
  if (cep.length !== 8) {
    throw new CepLookupError("invalid", "Informe um CEP válido com 8 dígitos.");
  }

  const controller = new AbortController();
  const abort = () => controller.abort();
  signal?.addEventListener("abort", abort, { once: true });
  if (signal?.aborted) controller.abort();
  const timeout = setTimeout(() => controller.abort(), 8000);

  try {
    const response = await fetch(`https://viacep.com.br/ws/${cep}/json/`, {
      signal: controller.signal,
      headers: { Accept: "application/json" },
      cache: "no-store",
    });
    if (!response.ok) {
      throw new CepLookupError("network", "Não foi possível consultar o CEP.");
    }

    const data = await response.json() as {
      erro?: boolean;
      logradouro?: string;
      bairro?: string;
      localidade?: string;
      uf?: string;
    };
    if (data.erro) {
      throw new CepLookupError("not_found", "CEP não encontrado. Você pode preencher o endereço manualmente.");
    }

    return {
      street: data.logradouro?.trim() ?? "",
      neighborhood: data.bairro?.trim() ?? "",
      city: data.localidade?.trim() ?? "",
      state: data.uf?.trim().toUpperCase() ?? "",
    };
  } catch (error) {
    if (error instanceof CepLookupError) throw error;
    if (error instanceof DOMException && error.name === "AbortError") {
      if (signal?.aborted) throw error;
      throw new CepLookupError("timeout", "A consulta do CEP demorou. Você pode preencher o endereço manualmente.");
    }
    throw new CepLookupError("network", "Não foi possível consultar o CEP. Você pode preencher o endereço manualmente.");
  } finally {
    clearTimeout(timeout);
    signal?.removeEventListener("abort", abort);
  }
}
