# ai-worker 的出口代理（AG-14）：只轉送 allowed-domains.txt 列的網域的 HTTPS（CONNECT），其他一律拒絕。
# 基底映像檔固定到 digest，標籤只是給人看的（Alpine 3.24，2026-11-26 查的；更新方式見 docs/DEPLOYMENT.md 第 7 節）。
# squid 的版本由這個 Alpine 版本決定。設定與網域清單不放進映像檔，由 compose 唯讀掛進來（改了重啟代理就生效，不用重建）。
FROM alpine:3.24@sha256:f59abe656b692d901e184be3445e084786602108599d2a51d229c0f5a48c71bf
RUN apk add --no-cache squid
# 不用 squid 預設的使用者與路徑：全部放 /tmp（compose 掛成 tmpfs），根目錄唯讀也跑得起來。
USER squid
ENTRYPOINT ["squid", "-N", "-f", "/etc/kumi-egress/squid.conf"]
