# حفظ الصور المتغيرة — 0.4.0

خيار «حفظ الصور حتى عند اختلاف بصمتها» مفعّل افتراضيًا. في وضع الفحص ثم الاختيار، يحفظ البايتات الحالية حتى لو تغيرت منذ الفحص. تظهر «محفوظة (متغيرة)» وتُحفظ بصمة الفحص ScannedSha256 وبصمة الملف المحفوظ Sha256 في scan-session.json.

هذا لا يصلح ترويسات الصور المفقودة ولا يعيد البيانات التي كُتب فوقها. لا يزال الفحص يكتشف JPEG/PNG المتصلة ذات البنية المقبولة. اختلاف البصمة لا يعني بالضرورة صورة سليمة.

## تجربة البرنامج
1. أغلق النسخة القديمة وثبّت Setup الجديد.
2. جرّب على USB تجريبي؛ اختر حفظ النتائج على قرص آخر.
3. اختر Deep وJPEG وPNG، ووضع الفحص ثم اختيار الصور.
4. اترك خيار حفظ الصور المتغيرة مفعّلًا، وافحص، وحدّد صورًا واضغط استرجاع المحدد.
5. افتح مجلد النتائج وتأكد من فتح الصور. الصور غير المتغيرة تظهر محفوظة؛ المتغيرة تظهر محفوظة (متغيرة).
6. لا تغيّر الفلاش عمدًا أثناء الاسترجاع. اختبار اختلاف البصمة يُجرى آليًا على بيانات تجريبية في الذاكرة:

```powershell
dotnet run --project ".\tests\ImageRescue.CoreTests\ImageRescue.CoreTests.csproj" -c Release
```

## تحديث الكود على GitHub
فك Source.zip في مجلد منفصل واضغط Update-Source.cmd وحدد مجلد المستودع الحالي. بعدها افتح الطرفية داخل المستودع:

```powershell
git status
git add src tests tools docs installer .github README.md Build-Release.ps1 Install-Update.ps1 Update-Source.cmd
git diff --cached --stat
git commit -m "Release v0.4.0: recover changed-hash images and retain both hashes"
git push
```

من Releases أنشئ إصدار v0.4.0 وارفع Setup.exe وwin-x64.zip. دفع الكود وحده لا يرفع المثبت.
