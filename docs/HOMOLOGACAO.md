# Homologação

Este documento descreve a configuração necessária para publicar a aplicação sem
armazenar credenciais ou valores específicos de ambientes no repositório.

## Backend no Render

O serviço deve usar o projeto `backend/LojaVirtual.csproj`. O Dockerfile da raiz
já publica esse projeto e a aplicação utiliza a variável `PORT` fornecida pelo
ambiente quando não existe uma sobrescrita explícita do ASP.NET Core.

Variáveis necessárias:

- `ASPNETCORE_ENVIRONMENT`: `Staging`.
- `ConnectionStrings__DefaultConnection`: connection string do MySQL de homologação.
- `MercadoPago__AccessToken`: credencial de teste do Mercado Pago.
- `MercadoPago__WebhookSecret`: segredo configurado para validar o webhook.
- `MercadoPago__WebhookUrl`: URL HTTPS pública do endpoint `/api/payments/mercadopago/webhook`.
- `Storefront__BaseUrl`: URL HTTPS pública do frontend.

Opcionalmente, para recuperação de pedido por e-mail:

- `Resend__Enabled`.
- `Resend__ApiKey`.
- `Resend__From`.
- `Resend__ReplyTo`.

Credenciais devem ser configuradas no gerenciador de environment variables do
Render. Não devem ser colocadas em `appsettings.json`, no Git ou em logs.

## Frontend na Vercel

Configure o diretório raiz do projeto como `frontend` e use os comandos padrão
do Next.js (`npm ci` e `npm run build`).

Variável necessária:

- `NEXT_PUBLIC_API_URL`: URL HTTPS pública do backend.

O valor local de `.env.local` não deve ser reutilizado em homologação.

## MySQL

O banco de homologação deve ser um banco MySQL separado do banco local e de
qualquer banco de produção. A connection string deve ser fornecida somente ao
Render por variável de ambiente.

As migrations devem ser aplicadas em uma etapa operacional autorizada, depois
de revisar o banco-alvo. Esta documentação não autoriza nem executa migrations.

## Checklist antes da publicação

- Confirmar que a migration `20261102174815_AddOrderAccessRecoveryTokens` e seu
  Designer estão versionados junto com o snapshot EF Core.
- Confirmar que o serviço responde na porta fornecida pelo Render.
- Confirmar `GET /api/products` usando a URL pública do backend.
- Confirmar que o frontend usa `NEXT_PUBLIC_API_URL` do backend de homologação.
- Confirmar URLs HTTPS públicas para storefront e webhook do Mercado Pago.
- Usar exclusivamente credenciais e pagamentos sandbox.
- Não depender de arquivos enviados em disco local para persistência de imagens.
