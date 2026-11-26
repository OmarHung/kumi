空的資料夾：沒設 DATAPROTECTION_DIR 時，compose 把它唯讀掛在 api 的 /run/secrets/dp，讓掛載點永遠存在。
不要把任何東西放進來。
