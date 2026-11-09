# Notificações de pedidos

As notificações usam o `ITransactionalEmailSender` já existente e não fazem
requisições ao Resend dentro das transações de pedido ou pagamento.

Os eventos são gravados em `OrderNotificationOutbox` junto com a transação que
confirma o estado correspondente. O worker `OrderNotificationOutboxWorker`
processa os eventos posteriormente, usando lease de dois minutos, tentativas e
retry com atraso progressivo.

A unicidade é garantida por `OrderId + EventType`. Pedidos sem e-mail válido não
geram evento. Não existe rotina de varredura retroativa: a migration cria apenas
a tabela vazia e eventos são inseridos somente nas novas operações.

Os eventos são:

- `order_created`;
- `payment_approved_preparing`;
- `order_shipped`;
- `order_delivered`.

O worker pode reprocessar uma mensagem se o processo cair depois de o Resend
aceitar o envio, mas antes de o registro ser marcado como processado. Essa é a
limitação inevitável sem uma confirmação transacional entre o banco e o
provedor. A chave única impede duplicações lógicas; a duplicação física nesse
intervalo deve ser tratada como possibilidade operacional do provedor.

No Render, mantenha as configurações existentes:

- `OrderNotifications__Enabled` — `false` por padrão; controla somente o worker da outbox;
- `Resend__Enabled`;
- `Resend__ApiKey`;
- `Resend__From`;
- `Resend__ReplyTo` (opcional).

A migration deve ser aplicada primeiro em staging, validada com
`Resend__Enabled=false` ou sender de teste, e só então aplicada em produção.

`OrderNotifications__Enabled=false` não desativa a recuperação de acesso. Ele
apenas impede que o worker consulte ou reivindique eventos da outbox; novos
eventos continuam sendo persistidos para processamento posterior.
