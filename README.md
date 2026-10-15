# Oz-translate

## Güncel sürümü indir

- [Windows için ZIP paketi](https://github.com/gorajde/Oz-translate/raw/refs/heads/fix/ocr-stability-security/downloads/Oz-translate-Windows.zip)
- [Doğrudan Oz-translate.exe](https://github.com/gorajde/Oz-translate/raw/refs/heads/fix/ocr-stability-security/Oz-translate.exe)
- [SHA-256 doğrulama dosyası](https://github.com/gorajde/Oz-translate/raw/refs/heads/fix/ocr-stability-security/downloads/SHA256SUMS.txt)

ZIP dosyasını bir klasöre çıkarıp `Oz-translate.exe` dosyasını çalıştırın. Windows ile başlatmayı ayarlardan seçebilirsiniz. Güncellerken önce eski programı tepsi menüsünden kapatın. Bu sürüm fareyle seçilen metni çevirmeyi ve tek pencerede görünüm ayarlarını içerir. Kaynak kod bu daldadır; `main` dalında birleştirme yapılmamıştır.

Fareyle ekran metnini okuyup Türkçeye çeviren Windows tepsi uygulaması. Windows'un yerleşik OCR motorunu, .NET Framework'ü ve Google Çeviri'nin ücretsiz uç noktasını kullanır.

## Kullanım

| İşlem | Nasıl yapılır? |
|---|---|
| Çeviri | Fareyi metnin üzerinde yaklaşık yarım saniye tutun; 5 satırlık bağlam seçilir |
| Seçilen metni çevir | Fareyle istediğiniz bölümü seçip sol fare düğmesini bırakın; yalnızca seçilen bölüm çevrilir |
| Daha uzun OCR çevirisi | OCR balonu açıkken **Tab** ile 15 satır seçin |
| Çeviriyi aç / kapat | **Ctrl + Alt + Z** veya tepsi menüsündeki **Aktif** |
| Ayarlar | Tepsideki **Ö** simgesine çift tıklayın veya menüden **Ayarlar** seçin |
| Ayarları gizle | Pencerenin **X** düğmesine basın; program tepside çalışır |
| Balonu taşı | Balonun üstündeki renkli tutamacı sürükleyin |
| Programdan çık | Tepsi menüsünden **Çıkış** seçin |

Balon metni odağı almaz ve fare tıklamalarını alttaki uygulamaya geçirir. Yalnızca ayrı tutamaç sürükleme için fareyi yakalar. Tutamaca ulaşmak için fare hareketinden sonra yaklaşık bir saniye süre tanınır. Balon veya tutamaç üzerindeyken OCR duraklar. Ayarlar açıkken OCR ve çeviriye özel Tab işlemi duraklar; ayarlardaki normal Tab gezinmesi çalışır.

## Seçilen metin

Metnin bir kelimesini, birkaç cümlesini veya birden fazla satırını fareyle seçin. Sol düğmeyi bıraktıktan sonra seçim okunur ve çeviri balonu seçimin yanında gösterilir. Seçim balonu açıkken Tab normal işlevini sürdürür; seçim çevresindeki yazılar çeviriye eklenmez. Uzun seçimler kelime sınırlarından parçalara bölünerek çevrilir.

Bu özellik, uygulamanın Windows UI Automation üzerinden metin seçimini sunmasını gerektirir. Panoya erişilmez ve Ctrl+C gönderilmez. Seçim sunulmuyorsa, boşsa veya ilgili uygulama yanıt vermiyorsa mevcut imleç/OCR davranışı devam eder. Çok kısa veya Türkçe seçimlerde mevcut dil ve aynı metin filtresi geçerlidir. Fareyle seçim sürerken OCR çalışmaz. Başka pencereye geçildiğinde eski seçimin çevirisi gösterilmez.

## Türkçe ve boş alan kontrolü

- İmlecin yakınında hem yatay hem dikey olarak bir metin satırı bulunmalıdır; boş alandaki uzak yazılar seçilmez.
- OCR'nin Türkçe karakterleri korunur. Kaynak dil otomatik algılanır.
- Önce imlecin altındaki satır kontrol edilir; çevredeki İngilizce satırların Türkçe hedef satırı bastırması önlenir.
- Türkçe algılanan, yalnızca rakam/noktalama içeren veya çevrildiğinde aynı kalan metin için balon gösterilmez. Aynı metin karşılaştırması büyük/küçük harf, boşluk, noktalama ve Unicode yazım farklarını dikkate almaz.
- Boş yanıt, eksik dil bilgisi veya servis hatasında hata metni içeren bir çeviri balonu açılmaz.

Kısa ya da OCR tarafından yanlış okunmuş metinlerde dil algılama yanılabilir. Çeviri internet gerektirir; seçilen satır ve bağlam çeviri servisine gönderilir. Aynı sonuçlar uygulama açıkken sınırlı bir önbellekte tutulur.

## Ayarlar

Tek ayarlar penceresinde balonun arka planı, yazı ve çerçeve renkleri, opaklığı (%30–100), yazı boyutu (8–28 punto) ve Windows ile başlatma tercihi bulunur. **Uygula** ile kaydedilir. Küçük puntoyla, küçük harflerle yazılmış kısayol ve kullanım açıklamaları gösterilir.

Görünüm ayarları `%LOCALAPPDATA%/Oz-translate/settings.json` dosyasına kaydedilir. Windows ile başlatma, geçerli kullanıcının `Run` kaydında tutulur. Yazma hataları pencerede bildirilir. **Varsayılanlara sıfırla**, görünümü koyu zemin / açık yazı / mavi çerçeve, %96 opaklık ve 12,5 puntoya döndürür; Windows ile başlatmayı kapatır ve kaydeder.

## Kurulum ve derleme

1. `Kur.bat` dosyasını çalıştırın. Program yoksa derlenir ve başlatılır.
2. Windows ile başlatmayı isterseniz ayarlardan açın. Kurulum mevcut tercihi değiştirmez.
3. Kaynak kod değiştikten sonra `build.bat` ile yeniden derleyin. Ek .NET SDK gerekmez; Windows'un .NET Framework C# derleyicisi ve WinMetadata dosyaları kullanılır.

Kaldırmak için `Kaldir.bat` dosyasını çalıştırın. Program kapanır ve başlangıç kaydı kaldırılır; program dosyaları ile kişisel görünüm ayarları korunur.

## Kontroller

PowerShell ile `tests/test.ps1` çalıştırılabilir. Bu kontroller dil/aynı metin filtresini, ayar doğrulamasını, JSON gidiş dönüşünü, pencereyi gizlemeyi ve gerçek Windows OCR ile imleç altında satır seçimini doğrular.

`tests/test.ps1 -PreviewUI -LiveTranslation` ayrıca kısa süreli test pencereleri açar; tek ayarlar örneğini, Ctrl+Alt+Z mesajını, Tab isteğini, tutamaç sürükleme olaylarını ve canlı İngilizce/Türkçe çeviri örneklerini kontrol eder. Gerçek tuş basımı yerine ilgili mesaj/olay yolları çağrılır. Başlangıç tercihi değiştirilmez. Görünüm görüntüsü `tests/artifacts/settings.png` konumuna yazılır.

Seçim testleri, gizli bir yerel RichTextBox kontrolünde gerçek UI Automation ile seçilen alt metnin okunmasını; çok satırlı seçimleri, eski/boş seçimlerin elenmesini ve uzun metnin eksiksiz parçalanmasını doğrular. Farklı tarayıcı, PDF ve masaüstü uygulamalarında fareyle uçtan uca seçim davranışı ayrıca denenmelidir.

Elle tamamlanacak ortam kontrolleri: farklı ekran ölçekleri ve çoklu monitör, gerçek klavye ile kısayollar, Windows oturum açılışında otomatik başlatma. OCR dil paketi bulunmazsa uygulama tepsi bildirimi gösterir.

## Dosyalar

`OzTranslate.cs` kaynak kod, `Oz-translate.exe` derlenmiş uygulama, `build.bat` derleyici, `Kur.bat` / `Kaldir.bat` kurulum yardımcılarıdır. Hata ayıklama günlüğü varsayılan olarak kapalıdır; açıldığında metin veya URL yerine yalnızca uzunluklar ve hata türleri yazılır.

## Lisans

Kişisel kullanım. Serbestçe kullanabilir ve değiştirebilirsiniz.
