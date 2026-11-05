<div align="center">

<img src="Ludoteca/Assets/LudotecaMark.png" width="96" alt="JOGOTECA" />

# JOGOTECA

**Seu backlog de jogos, finalmente organizado.**

Descubra lançamentos, monte suas listas e acompanhe tudo o que você joga,
quer jogar e já zerou, num app bonito, rápido e 100% no seu computador.

[**⬇️ Baixar para Windows (grátis)**](https://github.com/NicolasMurtha/Jogoteca/releases/latest/download/JOGOTECA-App-win-Setup.exe)

</div>

![Tela Descobrir da JOGOTECA](design/jogoteca-discovery-netflix.png)

## Por que a JOGOTECA?

Planilhas, notas no celular, prints esquecidos na galeria... A JOGOTECA junta
tudo num só lugar e transforma a pilha de jogos pendentes num catálogo que dá
vontade de abrir.

- **Descubra o que jogar** em faixas no estilo streaming: *Para você*,
  *Trending*, *Mais aguardados*, *Próximos lançamentos* e *Últimos lançamentos*.
- **Organize como um kanban:** arraste os jogos entre listas como *Quero
  jogar*, *Jogando* e *Finalizados*, ou crie as suas com nome, emoji e cor.
- **Saiba quanto tempo vai levar:** tempo estimado para zerar cada jogo.
- **Compare opiniões:** veja a nota do público ao lado da sua nota pessoal.
- **Registre sua jornada:** dê nota de 0 a 10, escreva reviews e marque as
  datas de início e de término.
- **Não perca lançamentos:** a JOGOTECA avisa quando um jogo da sua
  biblioteca é lançado.
- **Tema claro e escuro**, para jogar de dia ou de madrugada.
- **Leve seus dados com você:** exporte a biblioteca para CSV quando quiser.

![Listas em estilo kanban](design/jogoteca-kanban-hover.png)

## Seus dados são seus

- **Sem conta e sem nuvem:** tudo fica salvo no seu computador.
- **Funciona offline:** biblioteca, listas e cadastro manual não precisam de
  internet.
- **Chaves de API protegidas** pela criptografia do próprio Windows.

## Como começar

1. [Baixe o instalador](https://github.com/NicolasMurtha/Jogoteca/releases/latest/download/JOGOTECA-App-win-Setup.exe).
2. Execute o `JOGOTECA-App-win-Setup.exe`. A JOGOTECA é instalada em segundos,
   sem pedir permissão de administrador, e ganha atalhos na área de trabalho e
   no menu Iniciar.
3. Pronto! Comece adicionando jogos manualmente, só o nome já basta.

A JOGOTECA se atualiza sozinha: quando sair uma versão nova, ela avisa e
instala com um clique. Prefere não instalar? Use o
[arquivo portátil](https://github.com/NicolasMurtha/Jogoteca/releases/latest/download/JOGOTECA-App-win-Portable.zip).

> Se o Windows mostrar o aviso *"O Windows protegeu o computador"*, clique em
> **Mais informações** e depois em **Executar assim mesmo**.

### Catálogo online (opcional)

Para buscar jogos e ver capas, notas, tempos e lançamentos, conecte uma fonte
de dados gratuita em **Configurações**:

- **IGDB:** crie um *Client ID* e um *Client Secret* seguindo
  [este guia](https://api-docs.igdb.com/#account-creation).
- **RAWG:** crie uma chave em [rawg.io/apidocs](https://rawg.io/apidocs).

Com as duas configuradas, a JOGOTECA usa o IGDB e recorre ao RAWG
automaticamente quando precisar.

## Para desenvolvedores

Feita em WPF com .NET 10 e SQLite.

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O script executa os testes e gera o executável em
`dist\JOGOTECA-win-x64\JOGOTECA.exe`. Requer o SDK do .NET 10 no Windows.

Os dados ficam em `%LOCALAPPDATA%\JOGOTECA`. Instalações antigas continuam
usando `%LOCALAPPDATA%\Ludoteca` automaticamente, sem perder nada.

---

<div align="center">
<sub>Dados de jogos fornecidos por <a href="https://www.igdb.com/">IGDB</a> e <a href="https://rawg.io/">RAWG</a>.</sub>
</div>
