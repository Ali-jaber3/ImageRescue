# تجربة ImageRescue 0.4.0 وتحديث المشروع

## التشغيل أول مرة

1. أغلق النسخة القديمة. فك Source.zip في مجلد مستقل.
2. اضغط `Test-And-Run.cmd` داخل المجلد المستخرج. يلزم .NET 10 SDK الذي تستخدمه للبناء.
3. سيشغّل الاختبارات ثم يبني التطبيق. إذا نجحا يطلب Windows صلاحية المسؤول ويفتح البرنامج. إذا فشل، انسخ رسالة الخطأ؛ لا تتجاوز الاختبار.

بديل عبر Terminal في VS Code، بعد فتح **المجلد الذي يحتوي src وtests**:

```powershell
dotnet run --project ".\tests\ImageRescue.CoreTests\ImageRescue.CoreTests.csproj" -c Release
dotnet build ".\src\ImageRescue.App\ImageRescue.App.csproj" -c Release
Start-Process ".\src\ImageRescue.App\bin\Release\net10.0-windows\ImageRescue.App.exe" -Verb RunAs
```

## اختبار ملفات متعددة دون حذف بياناتك

1. اضغط `Create-Test-IMG.cmd` واختر مكانًا على فلاش تجريبي لحفظ IMG جديد. السكربت ينشئ ملفًا عاديًا ولا يفرمت الفلاش. يرفض استبدال ملف موجود.
2. افتح البرنامج واختر ملف IMG المنشأ، ثم اختر وجهة على **قرص فيزيائي آخر**، مثل قرص الكمبيوتر.
3. اضغط «جميع الأنواع»، واختر البحث العميق والفحص ثم اختيار الملفات.
4. ابدأ الفحص. عدد الملفات وأحجامها وبصماتها المتوقع موجود في ملف `.expected.json` بجوار IMG.
5. حدّد ملفات متنوعة، واسترجعها، ثم افتح PDF وWord والصور وشغّل الصوت والفيديو. قارن البصمات إن أردت التحقق من تطابق البايتات.
6. اختبر إلغاء تحديد نوع (مثل الصوت) ثم أعد الفحص؛ لا ينبغي استرجاع هذا النوع.
7. جرّب الإيقاف والاستئناف وإعادة التسمية. اضغط زر الشمس/القمر وتأكد من حفظ المظهر بعد إغلاق البرنامج وفتحه.

عينات DOC/XLS/PPT القديمة وDNG/CR2 داخل مجلد الاختبارات **عينات بنيوية اصطناعية** وليست ملفات إنتاج قابلة للفتح؛ لا يضعها سكربت IMG التجريبي. اختبر تلك الصيغ لاحقًا بملفات تملكها، خصوصًا RAW التجريبي.

## الصيغ المتاحة والحدود

- صور: JPG، PNG، BMP، GIF، WEBP، TIFF.
- مستندات: PDF، DOCX، XLSX، PPTX، ودعم محدود لـ DOC/XLS/PPT القديمة.
- فيديو: MP4، MOV، AVI.
- صوت: MP3، M4A، M4B، WAV.
- مضغوط: ZIP.
- صور كاميرا RAW تجريبية: DNG وCR2 فقط. المقصود RAW وليس ROW.

لا يوجد دعم لكل أنواع الملفات أو ضمان استرجاع كل ملف. الملفات المجزأة لا يعاد تجميعها. PDF يُستخرج حتى أول نسخة مكتملة ولا يضم التعديلات التراكمية اللاحقة. TIFF/RAW تجريبي وقد يفقد بيانات خاصة بالكاميرا. JPEG/PNG حتى 64 MiB، وبقية الملفات حتى 512 MiB. التفاصيل في FORMAT-SUPPORT.md.

## تحديث مستودعك

بعد نجاح التجربة، اضغط `Update-Source.cmd` داخل مجلد الإصدار الجديد، وحدّد مستودعك الحالي الذي يحتوي `.git`. تُحفظ نسخة احتياطية وتُستبدل ملفات المصدر دون تغيير تاريخ Git. لا تختَر مجلد الإصدار المستخرج نفسه.

داخل Terminal المستودع:

```powershell
git status
git add src tests tools docs installer .github README.md Build-Release.ps1 Install-Update.ps1 Update-Source.cmd Test-And-Run.ps1 Test-And-Run.cmd Create-Test-IMG.cmd
git diff --cached --stat
git commit -m "Add multi-format recovery and charcoal theme with a single theme toggle"
git push
```

## إنشاء Setup.exe

شغّل `Build-Release.ps1` بعد تثبيت NSIS على Windows. ينفذ الاختبارات ثم ينشر التطبيق ذاتيًا ويبني المثبّت. الناتج في `artifacts`.

يمكن أيضًا تشغيل workflow «Build Windows installer and portable ZIP» من صفحة Actions في مستودع GitHub بعد رفع التحديث. بعد نجاح التشغيل، نزّل Artifacts. إنشاء Release ورفع المثبّت للمستخدمين خطوة منفصلة. لا تنشر الإصدار قبل اختبار الواجهة والمثبّت على Windows.
