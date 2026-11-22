# Sincronizar o perfil com o Google (opcional)

Esta função guarda o **perfil** (configurações e resultados de benchmark) de cada computador na sua conta do Google, para você
ver, comparar e trazer para outro PC. **O programa funciona normalmente sem isso.** Nada é enviado sem você clicar.

## O que fica guardado e onde

- Um arquivo por computador (`perfil-<código aleatório>.pmpperfil`) na **pasta oculta do aplicativo** no seu Google Drive
  (`appDataFolder`). O programa só enxerga essa pasta: **não vê nenhum outro arquivo seu** (permissão `drive.appdata`).
- O nome do computador aparece na lista para você reconhecê-lo. O nome do **arquivo** usa um código aleatório, não o nome da máquina.
- Vão só as partes que você marcar. **Nunca** vão: chave da API, backups do Windows, perfis e programas dos jogos, pasta dos
  relatórios, nem os dados de login do Google.
- Para apagar: *Apagar deste PC da nuvem*. Ao remover o app da sua conta em
  [myaccount.google.com/permissions](https://myaccount.google.com/permissions), o Google apaga a pasta oculta.

## Configuração única (cerca de 10 minutos)

O Google exige que todo programa que entra em uma conta tenha um **ID de cliente**. É grátis. Quem compila o programa pode
embutir o dele; quem só usa o `.exe` cria o seu seguindo os passos abaixo.

1. Entre em <https://console.cloud.google.com/> com a sua conta e crie um **projeto novo** (nome livre, por exemplo "PC Monitor Pro").
2. Menu **APIs e serviços → Biblioteca**, procure **Google Drive API** e clique em **Ativar**.
3. Menu **APIs e serviços → Tela de permissão OAuth** (Google Auth Platform):
   - Tipo de usuário: **Externo**. Preencha o nome do app e o seu e-mail de suporte.
   - Em **Escopos**, adicione `.../auth/drive.appdata` (descrito como "ver e gerenciar os dados de configuração do app no Drive"),
     além de `openid` e `email`.
   - Em **Usuários de teste**, adicione o(s) seu(s) e-mail(s) do Google (até 100).
4. Menu **Credenciais → Criar credenciais → ID do cliente OAuth** e escolha o tipo **App para computador (Desktop)**.
   Copie o **ID do cliente** e o **segredo do cliente**.
5. No PC Monitor Pro, abra **Configurações → Sincronizar com o Google → Configuração de acesso**, cole o ID e o segredo e
   clique em **CONECTAR**. O navegador abre; entre na conta e autorize.

Na primeira vez o Google mostra "O Google não verificou este app". É esperado com um projeto seu em modo de teste:
**Avançado → Acessar PC Monitor Pro (não seguro)** (é o *seu* projeto).

### Limitações do modo de teste

- Segundo a documentação do Google, enquanto o projeto está em **Teste**, a permissão de uso contínuo **expira em 7 dias**:
  o programa pede para conectar de novo. Para evitar isso, publique o app ("Publicar app" na tela de permissão); como o escopo
  `drive.appdata` não é sensível, só exige a verificação básica (nome, página inicial e **política de privacidade** com endereço
  público).
- O segredo do cliente de apps de computador **não é secreto de verdade** (o Google sabe disso e usa PKCE); mesmo assim ele fica
  cifrado neste Windows.

## Como usar

- **Enviar perfil deste PC…** escolhe as partes e envia. Enviar de novo substitui o anterior deste mesmo PC.
- **Baixar perfil…** mostra os computadores que já enviaram (com data); escolha um e depois as partes a importar.
  O que você marcar substitui o atual; benchmarks são somados.
- **Desconectar** remove a permissão do Google e apaga os dados de login deste Windows. Os perfis na nuvem continuam; apague
  antes, se quiser removê-los.

## Segurança, em resumo

- Login pelo navegador (OAuth 2.0 com PKCE, retorno só em `127.0.0.1`, verificação do parâmetro `state`). O programa nunca vê
  sua senha.
- Os tokens ficam cifrados com a proteção de dados do Windows (DPAPI) e só este usuário deste Windows os lê.
- O arquivo baixado é tratado como não confiável (tamanho limitado, valores validados), igual a um perfil importado de arquivo.
