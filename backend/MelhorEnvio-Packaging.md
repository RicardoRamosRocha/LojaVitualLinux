# Melhor Envio - embalagem padrao do MVP

Cada cotacao usa uma unica embalagem padrao (`StandardPackage`) e um unico
volume enviado ao Melhor Envio. As dimensoes e o peso configurados sao da
embalagem completa. O sistema nao inventa dimensoes ou pesos individuais dos
produtos nem soma dimensoes de SKUs diferentes.

`Shipping__StandardPackage__MaxItems` representa a capacidade total de
unidades dentro da embalagem, e nao a quantidade de SKUs. Dois produtos
diferentes ou duas unidades do mesmo produto sao aceitos quando a soma das
quantidades nao excede esse limite.

Acima da capacidade, a cotacao e rejeitada antes da chamada externa com
`shipping_package_capacity_exceeded`. `MaxItems=1` explica a falha de carrinhos
com duas ou mais unidades; esse limite nao deve ser contornado sem validar uma
embalagem maior com dimensoes, peso e servicos homologados.

O MVP nao calcula cubagem ou peso por produto. Para aumentar `MaxItems`, a
capacidade fisica da embalagem padrao deve ser validada pela operacao e pelos
servicos habilitados no Melhor Envio.
