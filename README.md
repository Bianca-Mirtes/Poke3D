# 🧠 From Cards to Creatures – AR Module

**Mobile Augmented Reality for Interactive 3D Pokémon Card Experiences**

Este projeto implementa a parte de Realidade Aumentada (AR) do sistema **From Cards to Creatures**, que permite visualizar modelos 3D gerados por IA a partir de cartas físicas da franquia Pokémon. A aplicação foi desenvolvida utilizando Unity e é compatível com dispositivos móveis.

## 🎯 Objetivo

Transformar cartas físicas colecionáveis em criaturas 3D interativas em tempo real, sobrepondo os modelos diretamente sobre as cartas usando AR. O sistema utiliza visão computacional, reconhecimento de texto e modelos generativos 3D para criar experiências imersivas com mínima infraestrutura.

---

## 🚀 Funcionalidades

- 📸 **Detecção de Cartas em Tempo Real**  
  Utiliza YOLOv11 para localizar cartas no feed da câmera do celular.

- 🔍 **Extração de ID via OCR**  
  O número da carta (set + número) é extraído com OCR para identificar o modelo correspondente.

- 🧊 **Renderização AR de Criaturas 3D**  
  Os modelos são exibidos sobre a carta física em tempo real, utilizando ARFoundation.

- 🗃️ **Dataset Pré-compilado (Poke3D)**  
  Cada carta é mapeada para um modelo 3D gerado previamente via Hunyuan3D-2.1, garantindo baixa latência e alta fidelidade.

---

## 🛠️ Tecnologias Utilizadas

- **Unity** (2022.3 LTS)
- **ARFoundation** para suporte multiplataforma (Android/iOS)
- **YOLOv11** para detecção de cartas
- **PaddleOCR** para extração de IDs
- **Hunyuan3D-2.1** para geração dos modelos 3D
- **Poke3D Dataset** com ilustrações e modelos associados

---

## 🤳🏻 Exemplo de Uso
Aponte a câmera do celular para uma carta Pokémon física.

- O sistema detecta a carta, extrai o ID via OCR.

- O modelo 3D correspondente é carregado e sobreposto à carta.

- O usuário pode visualizar a criatura em diferentes ângulos.
