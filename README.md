# Ludoteca

Aplicativo desktop local para organizar um backlog pessoal de jogos. Feito em
WPF/.NET 8, com banco SQLite local e busca de catálogo pelo IGDB com fallback
automático para o RAWG.

## Recursos

- Criação e exclusão de listas personalizadas.
- Cadastro manual, exigindo apenas nome e lista.
- Jogos em uma ou várias listas ao mesmo tempo.
- Capa, nome, descrição, gêneros e ano de lançamento.
- Nota pessoal de 0 a 10, em intervalos de 0,5.
- Review pessoal por jogo.
- Busca no IGDB e fallback para o RAWG quando necessário.
- Cache local das capas para manter a biblioteca utilizável offline.
- Credenciais das APIs protegidas pelo Windows com DPAPI.
- Banco SQLite sem necessidade de servidor.

## Executar

Abra:

```text
dist\Ludoteca-win-x64\Ludoteca.exe
```

Nenhuma API é obrigatória. Use **Adicionar jogo** e escolha
**Adicionar manualmente** para cadastrar apenas o que souber.

As APIs são opcionais e servem para preencher capa, descrição, categoria e ano
automaticamente.

### RAWG

É a configuração mais simples. Crie uma chave em:

https://rawg.io/apidocs

Cole a chave no campo **RAWG API Key**.

### IGDB

O IGDB usa credenciais de uma aplicação Twitch. Siga:

https://api-docs.igdb.com/#account-creation

Informe o **Client ID** e o **Client Secret**. A Ludoteca solicita e renova o
token de acesso automaticamente.

Com apenas uma fonte configurada, somente ela é consultada. Quando as duas
fontes estiverem configuradas, a busca consulta o IGDB primeiro e usa o RAWG
como fallback.

## Dados locais

O banco, as credenciais criptografadas e as capas ficam em:

```text
%LOCALAPPDATA%\Ludoteca
```

Essa pasta também pode ser aberta pelo botão **Abrir pasta dos dados** dentro
das configurações.

## Compilar

Requer o SDK do .NET 8 no Windows. Execute:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O script executa os testes e publica uma versão autocontida para Windows x64.

## Estrutura

```text
Ludoteca\          Aplicativo WPF
Ludoteca.Tests\    Testes de persistência e fallback das APIs
design\            Fonte visual e captura de verificação
dist\              Executável publicado
```
