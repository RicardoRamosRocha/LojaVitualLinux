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
- `Cloudinary__CloudName`: `vnrcuol7`.
- `Cloudinary__ApiKey`: chave da API do Cloudinary.
- `Cloudinary__ApiSecret`: segredo da API do Cloudinary.

As três variáveis do Cloudinary devem ser configuradas somente no Environment do
Render. Imagens novas são enviadas para o Cloudinary; vídeos continuam no
armazenamento local da aplicação. Imagens legadas em `/images/products/` não são
migradas nem removidas automaticamente.

Para permitir chamadas diretas do frontend ao backend, configure a origem HTTPS
da Vercel no índice `Cors__AllowedOrigins__0`. A arquitetura atual usa proxies
Next.js e não depende de CORS no navegador, mas a política permanece explícita
e não aceita qualquer origem.

O endpoint público de liveness é:

- `/health`: não exige autenticação e não executa migrations.

Opcionalmente, para recuperação de pedido por e-mail:

- `Resend__Enabled`.
- `Resend__ApiKey`.
- `Resend__From`.
- `Resend__ReplyTo`.

Credenciais devem ser configuradas no gerenciador de environment variables do
Render. Não devem ser colocadas em `appsettings.json`, no Git ou em logs.

Para o MySQL Aiven, a connection string deve usar TLS com validação da CA, por
exemplo com os parâmetros `SslMode=VerifyCA` e `SslCa=<caminho-do-arquivo-ca>`.
O arquivo CA deve ser fornecido como secret file do ambiente, ou por outro
mecanismo seguro equivalente. Não usar `SslMode=None` em Staging ou produção.

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
- Confirmar `GET /health` retornando HTTP 200.
- Confirmar que `GET /` redireciona para `/Admin/Index`. Visitantes não
  autenticados devem ser encaminhados pelo ASP.NET Identity para
  `/Account/Login`; somente usuários autorizados pela policy `AdminOnly`
  podem acessar o painel.
- Confirmar `GET /api/products` usando a URL pública do backend.
- Confirmar que o frontend usa `NEXT_PUBLIC_API_URL` do backend de homologação.
- Confirmar URLs HTTPS públicas para storefront e webhook do Mercado Pago.
- Criar/editar um produto com uma imagem JPG/PNG/WEBP/GIF e confirmar que a
  URL HTTPS do Cloudinary aparece em `GET /api/products`.
- Remover uma mídia nova pelo painel e confirmar que ela deixa de aparecer no
  produto; imagens legadas devem continuar acessíveis e não devem ser apagadas.
- Confirmar que arquivos inválidos ou maiores que 10 MB são rejeitados sem
  criar registro de mídia.
- Usar exclusivamente credenciais e pagamentos sandbox.
- Não depender de arquivos enviados em disco local para persistência de imagens.
