# Recuperação de pedidos com Resend

O envio é desabilitado por padrão com `Resend:Enabled=false`. A API key nunca deve ser gravada neste repositório, no frontend ou em logs.

Para habilitar posteriormente em uma sessão local, configure variáveis de ambiente no processo do backend:

```powershell
$env:Resend__Enabled = "true"
$env:Resend__ApiKey = "re_..."
$env:Resend__From = "Angel Acessórios <pedidos@dominio-verificado.example>"
$env:Resend__ReplyTo = "atendimento@dominio-verificado.example"
```

O domínio usado em `From` deve estar verificado no Resend. O remetente, `Reply-To` e a chave devem ser fornecidos somente pelo ambiente de execução ou por um secret manager.

Para testes locais, mantenha `Resend__Enabled=false` e substitua `ITransactionalEmailSender` por um simulador no contêiner de testes. Os testes do projeto usam um sender em memória e um `HttpMessageHandler` simulado; não fazem chamadas ao Resend.

O fluxo de recuperação responde genericamente à solicitação, persiste somente o hash do token, expira em 15 minutos e consome o token atomicamente. A migration correspondente deve ser aplicada somente após revisão e autorização explícita.
