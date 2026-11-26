# backup 服務的映像檔：PostgreSQL 的映像檔（pg_dump 的版本和伺服器一致）加上 age（備份的選用加密，BACKUP_AGE_RECIPIENT）。
# 基底從 compose.yaml 的 x-postgres-image 帶進來（build args），所以固定的 digest 只有一個地方要更新。
# age 來自 Debian 的套件庫（映像檔的基底是 Debian 13），沒有另外下載的執行檔；授權是 BSD-3-Clause，只存在自己建的映像檔裡。
ARG POSTGRES_IMAGE
FROM ${POSTGRES_IMAGE}
RUN apt-get update \
    && apt-get install -y --no-install-recommends age \
    && rm -rf /var/lib/apt/lists/* \
    && age --version
