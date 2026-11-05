# Melhor Envio — configuração OAuth2

O frete real permanece desabilitado por padrão com `Shipping:RealQuotesEnabled=false`.
Não incluir tokens, client secret ou códigos de autorização neste repositório.

## Autorização inicial

1. Criar/configurar a aplicação no painel do Melhor Envio para o ambiente desejado.
2. Usar o `client_id`, `client_secret` e `redirect_uri` configurados na aplicação.
3. Solicitar autorização no fluxo OAuth2 oficial e obter o `authorization_code` no callback.
4. Trocar o código por `access_token` e `refresh_token` no endpoint OAuth oficial.
5. Configurar os tokens exclusivamente como variáveis protegidas no Render.

O serviço desta aplicação usa o `access_token` para consultar:

`POST {Shipping:BaseUrl}/api/v2/me/shipment/calculate`

Quando a API responder 401, o backend tenta renovar o token com `refresh_token` em:

`POST {Shipping:BaseUrl}/oauth/token`

O token renovado permanece somente em memória durante a execução do processo. Se o
provedor rotacionar o refresh token, o novo valor precisa ser persistido no mecanismo
de segredos do ambiente antes de reinícios ou novos deploys.

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
