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
- `DataProtection__CertificateBase64`: conteúdo Base64 do certificado PFX privado usado para criptografar as chaves de Data Protection. No Render, esta é a forma recomendada para evitar Secret File binário.
- `DataProtection__CertificatePath`: caminho do Secret File contendo o certificado
  privado usado para criptografar as chaves de Data Protection.
- `DataProtection__CertificatePassword`: senha do certificado, somente no
  ambiente do Render.
- `Cloudinary__CloudName`: `vnrcuol7`.
- `Cloudinary__ApiKey`: chave da API do Cloudinary.
- `Cloudinary__ApiSecret`: segredo da API do Cloudinary.

As três variáveis do Cloudinary devem ser configuradas somente no Environment do
Render. Imagens novas são enviadas para o Cloudinary; vídeos continuam no
armazenamento local da aplicação. Imagens legadas em `/images/products/` não são
migradas nem removidas automaticamente.

A aplicação persiste sessões e chaves de Data Protection no MySQL. O certificado
  de Data Protection deve ser fornecido como Secret File do Render e sua senha
  como variável protegida. Não armazenar o certificado, a senha ou as chaves no
  Git. Em Staging e produção, a aplicação não inicia sem esse certificado.

Para preparação local, os arquivos ficam fora do repositório em
`~/angel-secrets/`:

No Render, configure `DataProtection__CertificateBase64` com o conteúdo Base64
do PFX e `DataProtection__CertificatePassword` como variável protegida. Se o
Base64 não estiver configurado, a aplicação usa `DataProtection__CertificatePath`
como alternativa para um Secret File. Não armazenar o certificado, o Base64, a
senha ou as chaves no Git.

- `angel-dataprotection.key`
- `angel-dataprotection.crt`
- `angel-dataprotection-hml.pfx`

A pasta deve permanecer com permissão `700` e os arquivos com `600`. O arquivo
`.pfx` deve ser enviado ao Render como Secret File; depois, configure
`DataProtection__CertificatePath` para o caminho montado pelo Render e informe
`DataProtection__CertificatePassword` como variável secreta. Não copiar esses
arquivos para o projeto, para `frontend/public` ou para qualquer diretório
servido pela aplicação.

Para o Render, prefira `DataProtection__CertificateBase64` com o conteudo Base64
do PFX e `DataProtection__CertificatePassword` como variavel protegida. O
`DataProtection__CertificatePath` continua disponivel como alternativa para
Secret File. Nao armazenar certificado, Base64, senha ou chaves no Git.

O `POST /api/orders` possui limite de oito tentativas por dez minutos por
combinação do endereço observado pelo servidor e da sessão. O limite retorna
HTTP 429 e não confia em `X-Forwarded-For` enviado pelo cliente. Como o contador
é local ao processo, múltiplas instâncias podem ter limites independentes; se o
Render escalar horizontalmente, deve-se substituir esse limitador por um
contador distribuído antes de aumentar o tráfego.

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
Para a sessão persistente e as chaves protegidas, aplicar também as migrations
`AddPersistentSessionAndDataProtection` e `AddPersistentSessionCache` no banco
correto. A aplicação não executa migrations automaticamente.

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
