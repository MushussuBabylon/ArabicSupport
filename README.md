# مود ArabicSupport | دعم اللغة العربية للعبة RimWorld

<a id="languages"></a>

<p align="center">
  🇮🇶 <a href="#arabic">العربية</a> |
  🇬🇧 <a href="#english">English</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Language-Arabic-orange?style=flat-square" alt="Arabic">
  <img src="https://img.shields.io/badge/Game-RimWorld-blue?style=flat-square" alt="RimWorld">
  <img src="https://img.shields.io/badge/RimWorld-1.5%20%7C%201.6%20%7C%201.7-green?style=flat-square" alt="RimWorld 1.5, 1.6, 1.7">
  <img src="https://img.shields.io/badge/License-MIT-lightgrey?style=flat-square" alt="MIT License">
</p>

<a id="arabic"></a>

**التعريب:** [مستودع التعريب](https://github.com/MushussuBabylon/RimWorld-ar) | [Nexus Mods](https://www.nexusmods.com/rimworld/mods/757)  
**التنزيل:** [تنزيل أحدث إصدار](https://github.com/MushussuBabylon/RimWorld-ar/releases/tag/latest-release)

مود **ArabicSupport** هو ما يجعل اللغة العربية تعمل بشكل سليم في لعبة **RimWorld**. فاللعبة لا تعرض النصوص العربية وحدها كما ينبغي: يختل ترتيب الأسطر، وتضيع مواضع الحركات، ولا يمكن الكتابة بالعربية. يعالج المود هذه المشكلات، ويأتي مع خطوط عربية جاهزة، ويعمل مع التعريب ليظهر كل شيء داخل اللعبة بشكل صحيح.

---

## ماذا يفعل المود؟

- **التفاف صحيح للأسطر:** يقيس المود عرض النص الفعلي، فتنكسر الأسطر العربية بالترتيب الصحيح. ويبقى الاسم الإنجليزي (مثل اسم أحد المستعمرين) داخل الجملة العربية على سطر واحد.
- **الحركات في مكانها:** الفتحة والتنوين والشدة وغيرها تظهر فوق حروفها.
- **الكتابة بالعربية:** اكتب بالعربية في أسماء المستعمرين والحيوانات ومربعات البحث ونوافذ إعادة التسمية. تُربط الحروف وتُرتب أثناء الكتابة، وتعثر مربعات البحث على الكلمات العربية.
- **خطوط عربية جاهزة:** عشرة خطوط عريضة، والخط الافتراضي هو **Cairo Bold**. يمكنك تغييره من إعدادات المود أو إضافة خطوط أخرى.
- **لا تنقطع النقاط والذيول:** لكل خط ارتفاع سطر مضبوط، حتى لا تُقص نقطتا الياء ولا ذيول الراء والواو والقاف.
- **إعدادات بسيطة:** نافذة إعدادات بتبويبات ومعاينة مباشرة، وكل تغيير يظهر أثره فورا.
- **خفيف:** يعتمد على Harmony ولا يستهلك إلا القليل من الموارد.

---

## المتطلبات

- RimWorld **1.5** أو **1.6** أو **1.7** (يختار المود الملفات المناسبة لإصدارك تلقائيا).
- مود **Harmony**.
- التعريب العربي مفعّلا من إعدادات اللغة.

---

## طريقة التثبيت

1. حمّل [أحدث إصدار](https://github.com/MushussuBabylon/RimWorld-ar/releases/tag/latest-release).
2. فك ضغط `ArabicSupport.zip` مباشرة داخل مجلد اللعبة الرئيسي `RimWorld`.
3. فعّل مود `ArabicSupport` من قائمة المودات داخل اللعبة (يتطلب مود `Harmony`). ويمكنك أيضا تفعيل `Arabic Auto Translation` لترجمة المودات الأخرى.
4. اختر اللغة العربية من قائمة اختيار اللغة داخل اللعبة.

### المسارات الافتراضية

**Steam:**
`C:\Program Files (x86)\Steam\steamapps\common\RimWorld`

**GOG:**
`C:\GOG Games\RimWorld`

---

## الإعدادات

من *Options ← Mod settings ← Arabic Support*. تعمل الإعدادات الافتراضية جيدا لمعظم اللاعبين، ولا تحتاج إلى تغيير شيء إلا إذا أردت.

| التبويب | ما يمكنك تغييره |
|---|---|
| الخط المخصص | اختيار الخط، ووضع الحركات على حروفها |
| الحجم | حجم الخط وموضعه العمودي |
| التخطيط | التفاف الأسطر، وإبقاء الكلمات الإنجليزية معا، وقص الكلمات الطويلة، ومساحة النقاط والذيول |
| الكتابة | تشغيل الكتابة بالعربية، مع مربع لتجربتها |
| المعاينة | شكل الخط والتفاف النص قبل أن تغلق النافذة |
| أدوات | مسح ذاكرة النصوص المؤقتة إذا بقي نص ما يبدو خاطئا |

---

## الخطوط

الخط الافتراضي هو **Cairo Bold**. ويأتي مع المود تسعة خطوط عريضة أخرى يمكن اختيارها من إعدادات المود: Almarai وEl Messiri وIBM Plex Sans Arabic وMada وNoto Kufi Arabic وNoto Sans Arabic وRubik وTajawal وVazirmatn.

لإضافة خط آخر من [مكتبة الخطوط](FontLibrary/README.md) (73 نمطا إضافيا):

1. حمّل ملف الخط (`.fontbundle`) الذي تريده من مجلد [FontLibrary/Fonts](FontLibrary/Fonts). (في RimWorld 1.7 تعمل أيضا ملفات `.ttf` و`.otf` الخاصة بك.)
2. الصقه داخل مجلد `Fonts` الموجود في مجلد المود.
3. شغّل اللعبة واختر الخط من إعدادات المود.

---

## التوافق والإبلاغ عن المشكلات

- إذا واجهت مشكلة، جرّب تحميل هذا المود **بعد** مودات الواجهة أو الترجمة الأخرى.
- إذا استمرت المشكلة، افتح بلاغا (Issue) مع إرفاق: إصدار اللعبة، ولقطة شاشة، وقائمة المودات التي تستخدمها.

---

## الشكر والترخيص

- **المؤلف:** Mushussu Babylon
- **شكر خاص:** لمكتبة Harmony ولمجتمع مودرات RimWorld.
- **الخطوط:** من Google Fonts، بموجب رخصة SIL Open Font License (انظر مجلد [Licenses](Licenses)).
- **المود:** بموجب [رخصة MIT](LICENSE)، يمكنك استخدامه وتعديله وتوزيعه بحرية مع ذكر المصدر.

---

**أُهدي هذا العمل إلى جميع لاعبي RimWorld الناطقين بالعربية. استمتعوا باللعبة!**

<p align="center"><a href="#languages">⬆ العودة إلى اختيار اللغة</a></p>

---

<a id="english"></a>

# 🇬🇧 ArabicSupport: Arabic support for RimWorld

**Translation:** [Translation repository](https://github.com/MushussuBabylon/RimWorld-ar) | [Nexus Mods](https://www.nexusmods.com/rimworld/mods/757)  
**Download:** [Download Latest Release](https://github.com/MushussuBabylon/RimWorld-ar/releases/tag/latest-release)

**ArabicSupport** is what makes Arabic work properly in **RimWorld**. The game does not display Arabic correctly on its own: lines break in the wrong order, harakat lose their place, and you cannot type Arabic. This mod fixes those problems, comes with ready-made Arabic fonts, and works together with the Arabic translation so everything in the game looks right.

---

## What does it do?

- **Correct line wrapping:** the mod measures the real width of the text, so Arabic lines break in the right order. An English name inside an Arabic sentence (such as a colonist's name) stays on one line.
- **Harakat in the right place:** fatha, tanween, shadda and the rest sit on top of their letters.
- **Typing in Arabic:** type Arabic in colonist and animal names, search boxes and rename windows. Letters are joined and ordered as you type, and search boxes find Arabic words.
- **Ready-made Arabic fonts:** ten bold fonts, with **Cairo Bold** as the default. Change it in the mod settings, or add more fonts.
- **No cut-off dots or tails:** every font has its own line height, so the dots of the yeh and the tails of reh, waw and qaf are not clipped.
- **Simple settings:** a tabbed settings window with a live preview. Every change shows at once.
- **Lightweight:** built on Harmony, with very little impact on performance.

---

## Requirements

- RimWorld **1.5**, **1.6** or **1.7** (the mod picks the right files for your version automatically).
- The **Harmony** mod.
- The Arabic translation turned on in the language settings.

---

## Installation

1. Download the [latest release](https://github.com/MushussuBabylon/RimWorld-ar/releases/tag/latest-release).
2. Extract `ArabicSupport.zip` directly into your main `RimWorld` game directory.
3. Enable `ArabicSupport` from the in-game Mods menu (requires `Harmony`). You can also enable `Arabic Auto Translation` to translate other mods.
4. Select Arabic from the in-game language selection menu.

### Default Installation Paths

**Steam:**
`C:\Program Files (x86)\Steam\steamapps\common\RimWorld`

**GOG:**
`C:\GOG Games\RimWorld`

---

## Settings

Open *Options → Mod settings → Arabic Support*. The defaults work well for most players, so you only need to change something if you want to.

| Tab | What you can change |
|---|---|
| Custom font | Pick the font, and place harakat on their letters |
| Size | Font size and vertical position |
| Layout | Line wrapping, keeping English words together, cutting very long words, room for dots and tails |
| Typing | Turn Arabic typing on, with a box to try it |
| Preview | See the font and the text wrapping before you close the window |
| Tools | Clear the text cache if some text still looks wrong |

---

## Fonts

The default font is **Cairo Bold**. Nine more bold fonts come with the mod and can be picked in the mod settings: Almarai, El Messiri, IBM Plex Sans Arabic, Mada, Noto Kufi Arabic, Noto Sans Arabic, Rubik, Tajawal and Vazirmatn.

To add another font from the [font library](FontLibrary/README.md) (73 extra styles):

1. Download the font file (`.fontbundle`) you want from the [FontLibrary/Fonts](FontLibrary/Fonts) folder. (On RimWorld 1.7, your own `.ttf` and `.otf` files work too.)
2. Paste it into the `Fonts` folder inside the mod folder.
3. Start the game and pick the font in the mod settings.

---

## Compatibility and Bug Reports

- If you run into problems, try loading this mod **after** other UI or translation mods.
- If the problem continues, open an Issue and include: your game version, a screenshot, and your mod list.

---

## Credits and License

- **Author:** Mushussu Babylon
- **Special thanks:** to the Harmony library and the RimWorld modding community.
- **Fonts:** from Google Fonts, under the SIL Open Font License (see the [Licenses](Licenses) folder).
- **Mod:** released under the [MIT License](LICENSE). You are free to use, modify and share it, with credit to the source.

---

**Made for all Arabic-speaking RimWorld players. Enjoy the game!**

<p align="center"><a href="#languages">⬆ Back to language selection</a></p>
