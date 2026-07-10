# VillageOS managed-microservice example — Python (FastAPI).
FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY app.py ./
# Mycelium launches handlers as local daemons; this binds localhost like the
# .NET examples. For containerized runs, bind 0.0.0.0 and use host networking.
ENTRYPOINT ["python", "app.py"]
