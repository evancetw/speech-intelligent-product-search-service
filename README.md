# Speech at .NET Conf 2024 Taiwan

## 課程主題

逐步實現智慧商品搜尋系統：從關鍵字搜尋到向量資料庫與 AI 整合

## 課程大綱

在這場演講中，我們一起探索如何打造一個智慧、精準的商品搜尋系統！從基礎建設到利用 AI 強化搜尋體驗，我將帶領大家一步步實現一套符合現代需求的搜尋解決方案。
🚀 基礎建設：以 ASP.NET Core 與 Elasticsearch 基於 Azure 快速建立穩定的搜尋架構。
🔍 精準搜尋：實作關鍵字搜尋與多層過濾，提升用戶搜尋體驗。
🤖 AI 智慧化：結合向量資料庫與 Azure OpenAI，賦予搜尋系統理解語意的能力，實現語意搜尋與個性化推薦。
不論您是搜尋系統的新手還是尋求創新解決方案的專家，這場演講將帶給您啟發與實作方向！

## 課程目的

透過 Strong Buy 購物網站，展示一個智慧商品搜尋系統的演進，讓用戶能夠快速、精準地找到他們想要的商品。

- 熟悉 .NET 開發
- 熟悉 Azure 服務
- 熟悉 Elasticsearch
- 熟悉 Azure OpenAI
- 熟悉向量資料庫
- 熟悉 Azure 的 AI 服務

## 各階段實作

### Phase 1：基礎建設

商品資料結構：

```json
{
  "id": 1,
  "name": "商品名稱",
  "description": "商品描述",
  "price": 100,
  "category": "category1",
  "subcategories": ["subcategory1", "subcategory2"],
  "brand": "brand1",
  "color": "red",
  "size": "M",
  "material": "cotton",
  "image": "https://example.com/image.jpg",
  "images": [
    "https://example.com/image1.jpg",
    "https://example.com/image2.jpg"
  ],
  "tags": ["tag1", "tag2"],
  "attributes": {
    "attribute1": "value1",
    "attribute2": "value2"
  },
  "reviews": [
    {
      "rating": 5,
      "comment": "This is a great product!"
    }
  ],
  "created_at": "2024-01-01",
  "updated_at": "2024-01-01"
}
```

網站功能：

- 商品目錄 (catalog)
  - 全部商品
  - 依據分類列出商品
    - 依據子分類列出商品
- 商品搜尋 (search)
- 商品篩選 (filter)

資料庫：使用 SQLite 資料庫，透過 EF Core 進行資料庫操作。

商品圖片：使用 https://fakeimg.pl/300x300/?retina=1&font=noto&text=%E5%95%86%E5%93%81%E5%90%8D%E7%A8%B1 產生

### Phase 2：精準搜尋

- 關鍵字搜尋
- 多層過濾

選了分類：只會列出該分類的商品

可以過濾的項目如下：

- 價格範圍：以 500 元為單位，例如 0-500 元、500-1000 元、1000-1500 元、1500-2000 元，最後一項為 10000 元以上
- 品牌：依據關鍵字搜尋 + 分類的搜尋結果做 distinct
- 顏色：依據關鍵字搜尋 + 分類的搜尋結果做 distinct
- 尺寸：依據關鍵字搜尋 + 分類的搜尋結果做 distinct
- 材質：依據關鍵字搜尋 + 分類的搜尋結果做 distinct
- (optional) 標籤：依據關鍵字搜尋 + 分類的搜尋結果做 distinct

### Phase 3：AI 智慧化

### Phase 4：終局

網站功能：

- 商品目錄 (catalog)
  - 全部商品
  - 依據分類列出商品
    - 依據子分類列出商品
- 商品搜尋 (search)
  - 關鍵字搜尋
  - 語意搜尋
- 商品篩選 (filter)
  - 依據價格篩選
  - 依據品牌篩選
  - 依據顏色篩選
  - 依據尺寸篩選
  - 依據材質篩選
- 商品排序
  - 依據價格排序
  - 依據評價排序
  - 依據上架時間排序
  - 依據搜尋結果相關度排序
- 商品推薦 (recommend)
  - 熱門關鍵字推薦
  - 依據用戶行為推薦
  - 依據商品屬性推薦
  - 依據商品評價推薦
  - 依據商品上架時間推薦
  - 依據商品熱門度推薦
  - 訪客推薦（未登入的瀏覽者）
  - 個人化推薦（已登入的用戶）
    - 依據用戶喜好推薦
    - 依據用戶瀏覽紀錄推薦
    - 依據用戶購買紀錄推薦
    - 依據用戶評價紀錄推薦
    - 依據用戶搜尋紀錄推薦
    - 依據用戶瀏覽紀錄推薦
    - 依據用戶購買紀錄推薦
    - 依據用戶評價紀錄推薦
    - 依據用戶搜尋紀錄推薦

部署

- 使用 Azure App Service 部署 ASP.NET Core 網站
- 使用 Azure Elasticsearch 部署 Elasticsearch
- 使用 Azure OpenAI 部署 Azure OpenAI
- （？）使用 Azure 向量資料庫部署向量資料庫
