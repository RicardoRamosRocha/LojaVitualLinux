# Melhor Envio — configuração OAuth2

O frete real permanece desabilitado por padrão com `Shipping:RealQuotesEnabled=false`.
Não incluir tokens, client secret ou códigos de autorização neste repositório.

## Autorização inicial

1. Criar/configurar a aplicação no painel do Melhor Envio para o ambiente desejado.
2. Usar o `client_id`, `client_secret` e `redirect_uri` configurados na aplicação.
3. Solicitar autorização no fluxo OAuth2 oficial e obter o `authorization_code` no callback.
4. Trocar o código por `access_token` e `refresh_token` no endpoint OAuth oficial.
5. Configurar client ID, client secret, redirect URI e escopo exclusivamente como variáveis protegidas no Render.

O início da autorização é protegido pela política administrativa em:

`POST /api/integrations/melhorenvio/authorize`

O callback público é:

`GET /api/integrations/melhorenvio/callback`

Para homologação, registrar exatamente:

`https://angel-acessorios-api-hml.onrender.com/api/integrations/melhorenvio/callback`

O serviço desta aplicação usa o `access_token` para consultar:

`POST {Shipping:BaseUrl}/api/v2/me/shipment/calculate`

Quando a API responder 401, o backend tenta renovar o token com `refresh_token` em:

`POST {Shipping:BaseUrl}/oauth/token`

O state e os tokens são persistidos na tabela de cache distribuído já existente,
protegidos pelo Data Protection da aplicação. O consumo do state é transacional e
remove o registro antes da troca do código, impedindo reutilização. O refresh token
renovado é persistido junto com o novo access token e não é enviado ao navegador.

## Variáveis protegidas

Configurar no Render, sem prefixar com `NEXT_PUBLIC_`:

- `Shipping__RealQuotesEnabled`
- `Shipping__BaseUrl`
- `Shipping__OriginPostalCode`
- `Shipping__AccessToken`
- `Shipping__RefreshToken`
- `Shipping__ClientId`
- `Shipping__ClientSecret`
- `Shipping__RedirectUri`
- `Shipping__UserAgent`
- `Shipping__QuoteTtlMinutes`
- `Shipping__StandardPackage__Enabled`
- `Shipping__StandardPackage__MaxItems`

Referências oficiais: [autenticação](https://docs.melhorenvio.com.br/docs/autenticacao),
[fluxo de autorização](https://docs.melhorenvio.com.br/reference/fluxo-de-autoriza%C3%A7%C3%A3o)
e [cotação de fretes](https://docs.melhorenvio.com.br/docs/cotacao-de-fretes).
