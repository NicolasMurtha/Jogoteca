# JOGOTECA

Aplicativo desktop local para organizar um backlog pessoal de jogos. Feito em
WPF/.NET 8, com banco SQLite local e catálogo opcional pelo IGDB com fallback
automático para o RAWG.

## Recursos

- Tela Descobrir em formato de streaming, com faixas horizontais empilhadas.
- Seções Para você, Trending games, Mais aguardados, Próximos lançamentos e Últimos lançamentos.
- Biblioteca independente: todo jogo adicionado entra primeiro nela.
- Organização por drag-and-drop dos cards para as listas.
- Criação, edição e exclusão de listas com nome, emoji e cor.
- Cadastro manual exigindo somente o nome.
- Capa, nome, descrição, gêneros e ano de lançamento opcionais.
- Nota pessoal de 0 a 10 e review por jogo.
- Busca no IGDB e fallback para o RAWG quando ambos estão configurados.
- Funcionamento com apenas uma das APIs configurada ou sem nenhuma API.
- Cache local das capas e credenciais protegidas pelo Windows com DPAPI.

## Executar

Abra:

```text
dist\JOGOTECA-win-x64\JOGOTECA.exe
```

Nenhuma API é obrigatória. Use **Adicionar jogo** e
**Adicionar manualmente** para cadastrar apenas o que souber.

### RAWG

Crie uma chave em:

https://rawg.io/apidocs

Cole a chave no campo **RAWG API Key**.

### IGDB

Crie as credenciais de uma aplicação Twitch seguindo:

https://api-docs.igdb.com/#account-creation

Informe o **Client ID** e o **Client Secret**. Com apenas uma fonte configurada,
somente ela é consultada. Com as duas, o IGDB é usado primeiro e o RAWG entra
como fallback.

## Dados locais

Novas instalações guardam o banco, as capas e as configurações em:

```text
%LOCALAPPDATA%\JOGOTECA
```

Instalações atualizadas continuam usando `%LOCALAPPDATA%\Ludoteca`
automaticamente, preservando todos os dados da versão anterior.

## Compilar

Requer o SDK do .NET 8 no Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O script executa os testes e publica uma versão autocontida para Windows x64.

## Estrutura

```text
Ludoteca\          Aplicativo WPF
Ludoteca.Tests\    Testes de persistência, catálogo e fallback das APIs
design\            Capturas de verificação visual
dist\              Executável publicado
```
