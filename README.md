# Oz-translate

Windows için ekran metni ve fareyle seçilen metni Türkçeye çeviren tepsi uygulaması.

## Güncel sürümü indir

- **[Windows ZIP paketini indir](https://github.com/gorajde/Oz-translate/raw/refs/heads/fix/ocr-stability-security/downloads/Oz-translate-Windows.zip)**
- **[Oz-translate.exe dosyasını indir](https://github.com/gorajde/Oz-translate/raw/refs/heads/fix/ocr-stability-security/Oz-translate.exe)**
- [SHA-256 doğrulama dosyası](https://github.com/gorajde/Oz-translate/raw/refs/heads/fix/ocr-stability-security/downloads/SHA256SUMS.txt)
- [Güncel kaynak kod ve ayrıntılı kullanım](https://github.com/gorajde/Oz-translate/tree/fix/ocr-stability-security)

ZIP dosyasını bir klasöre çıkarıp `Oz-translate.exe` dosyasını çalıştırın. Güncellerken önce eski programı tepsi menüsünden kapatın. Windows ile başlatmayı ayarlardan seçebilirsiniz.

## Kullanım

| İşlem | Nasıl yapılır? |
|---|---|
| Metin çevirisi | Fareyi metnin üzerinde kısa süre tutun |
| Seçilen bölümü çevir | Fareyle metni seçip sol düğmeyi bırakın |
| Daha geniş OCR bağlamı | OCR balonu açıkken Tab |
| Çeviriyi aç / kapat | Ctrl + Alt + Z |
| Ayarları aç | Tepsideki Ö simgesine çift tıklayın |
| Ayarları gizle | X düğmesine basın |
| Balonu taşı | Üstündeki tutamacı sürükleyin |

Ayarlar tek pencerede renk, saydamlık, yazı boyutu, Windows ile başlatma ve varsayılanlara sıfırlama seçeneklerini içerir. Türkçe algılanan, boş veya çevirisi aynı kalan metinde çeviri balonu gösterilmez.

Seçilen metni okuma, uygulamanın Windows UI Automation desteğine bağlıdır; destek sunulmuyorsa mevcut OCR davranışı sürer. Pano değiştirilmez. Çeviri için internet gerekir ve seçilen metin Google Çeviri servisine gönderilir.

Güncel sürüm derlenmiş ve 46 otomatik kontrolden geçmiştir. Farklı tarayıcı/PDF uygulamalarındaki fareyle uçtan uca davranış ayrıca denenmelidir.

## Dal bilgisi

Eski `main/Oz-translate.exe` indirmesi kaldırıldı. Bu sayfadaki bağlantılar güncel sürüme gider. `main` dalında kalan kaynak ve kurulum dosyaları önceki sürüme aittir; güncel kaynak ve paket için yukarıdaki bağlantıları kullanın. Geliştirme dalı `main` ile birleştirilmedi; Git geçmişi korunur.
