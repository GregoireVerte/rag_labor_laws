FROM n8nio/n8n:latest

# Przełączamy na root, żeby ustawić port i uprawnienia
USER root

# Wymuszamy port 7860 (standard Hugging Face)
ENV N8N_PORT=7860
EXPOSE 7860

# Nie nadpisujemy CMD - pozwalamy n8n użyć jego domyślnego mechanizmu startowego,
# który wie dokładnie, gdzie szukać komendy "n8n".