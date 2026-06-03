# Oz-translate

**Fareyle anlık İngilizce → Türkçe ekran çevirisi.** Fare imlecini ekrandaki herhangi bir İngilizce yazının üzerine getirin; o satırların Türkçe çevirisi hemen yanında küçük bir balonda belirir. Masaüstü programlarında **ve** tarayıcılarda çalışır.

> Windows'un kendi OCR motoruyla ekrandaki yazıyı "görerek" okur, çeviriyi Google Çeviri ile yapar. **Hiçbir ek kurulum gerektirmez** — yalnızca Windows'un yerleşik bileşenlerini kullanır (.NET Framework + Windows.Media.Ocr).

## Özellikler

- 🖱️ Fare nerede dururs orada **5 satırlık** kısmı çevirir; fare kaydıkça çeviri dinamik olarak yenilenir, eski balon hemen kapanır.
- ⌨️ **Tab** tuşu: daha geniş bağlam için 5 + 10 = **15 satır** çevirir.
- 🪟 Çeviri balonu orijinal metnin **üstünü kapatmaz** (yana açılır) ve **tıklama geçişlidir** (click-through) — altındaki yazıyı tıklamayı engellemez.
- 🚀 Bilgisayar açılınca **otomatik başlar**.
- 🔘 **Ctrl + Alt + Z** ile veya tepsi menüsünden aç/kapat.
- 🔤 Koyu zeminli, okunaklı (Segoe UI) çeviri penceresi.

## Kurulum

1. `Kur.bat` dosyasına çift tıklayın.
2. Program derlenir, başlatılır ve Windows açılışına eklenir.

Kaldırmak için: `Kaldir.bat`.

## Kullanım

| İşlem | Nasıl |
|---|---|
| Çeviri | Fareyi İngilizce yazının üzerinde ~yarım saniye tutun |
| Daha uzun çeviri | Yazının üzerindeyken **Tab** |
| Aç / kapat | **Ctrl + Alt + Z** veya tepsi ikonuna çift tık |
| Menü | Tepsi ikonuna sağ tık (Aktif / Açılışta başlat / Çıkış) |

## Dosyalar

| Dosya | Açıklama |
|---|---|
| `OzTranslate.cs` | Kaynak kod (tek dosya, C#) |
| `Oz-translate.exe` | Derlenmiş program |
| `build.bat` | Yeniden derleme |
| `Kur.bat` / `Kaldir.bat` | Kurulum / kaldırma |
| `OKUBENI.txt` | Ayrıntılı kullanım kılavuzu |

## Nasıl çalışır?

1. Fare durduğunda imlecin çevresindeki ekran bölgesi yakalanır.
2. Görüntü 2× büyütülüp Windows OCR ile metne çevrilir (küçük puntoda doğruluk için).
3. İmlecin bulunduğu sütundaki 5 (veya Tab ile 15) satır seçilir.
4. Metin Google Çeviri'ye gönderilir, Türkçesi balonda gösterilir.

## Notlar

- Yalnızca **İngilizce → Türkçe** yönünde çalışır ve **internet** gerektirir.
- OCR tabanlıdır; çok küçük puntoda nadiren harf hatası olabilir.
- Google'ın ücretsiz çeviri ucunu kullanır; çok yoğun kullanımda geçici sınırlama olabilir (aynı metin kısa süre önbelleğe alınır).

## Lisans

Kişisel kullanım. Serbestçe kullanabilir ve değiştirebilirsiniz.
