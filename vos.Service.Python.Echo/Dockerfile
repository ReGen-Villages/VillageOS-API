# VillageOS managed-microservice example — Python (FastAPI).
# requirements.txt installs vos.Service.Python.Shared from beside this folder, so build from the
# repository root:  docker build -f vos.Service.Python.Echo/Dockerfile -t vos-python-echo .
FROM python:3.12-slim
WORKDIR /app
# Kept at the sibling path requirements.txt names, relative to /app
COPY vos.Service.Python.Shared /vos.Service.Python.Shared
COPY vos.Service.Python.Echo/requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY vos.Service.Python.Echo/app.py ./
# Mycelium launches handlers as local daemons; this binds localhost like the
# .NET examples. For containerized runs, bind 0.0.0.0 and use host networking.
ENTRYPOINT ["python", "app.py"]
