# Locora

[English README](README_EN.md)

เครื่องมือจัดการสภาพแวดล้อมพัฒนาเว็บบน Windows ในแนวเดียวกับ Laragon ที่พัฒนาบน `Microsoft.UI.Xaml` / WinUI 3

- [แผนความสามารถเทียบเท่า Laragon](docs/locora-laragon-parity-plan.md)
- [โครงสร้างระยะที่ 1](docs/phase-1-scaffold.md)
- [เช็กลิสต์ขอบเขตโปรเจกต์](docs/project-scope-checklist.md)

## สถานะปัจจุบัน

- โซลูชันแบบหลายโปรเจกต์: `Locora.App`, `Locora.Supervisor`, `Locora.Application`, `Locora.Infrastructure`, `Locora.Domain`, `Locora.App.Contracts`
- มีโครง shell ของ WinUI 3 พร้อมหน้า Dashboard, Services, Domains & Hosts, Diagnostics, Logs และ Settings
- แถบบนของ shell มี command palette (`Ctrl+K`) สำหรับนำทางระหว่างหน้า เรียก action ของ supervisor ที่ใช้บ่อย และสั่งงานระดับโปรเจกต์
- ระบบแจ้งเตือนระดับ shell แสดง toast สำหรับผลลัพธ์สำเร็จ คำเตือน และข้อผิดพลาดได้จากทุกหน้า
- เมื่อรายการ service, project, diagnostic หรือ activity ยังว่าง ระบบจะแสดง recovery card พร้อม action ถัดไปที่ปลอดภัยที่สุด
- Dashboard มีคู่มือการตั้งค่าครั้งแรกแบบคงอยู่ ซึ่งสามารถซ่อนหรือเรียกกลับมาได้จาก Settings
- Settings ใช้กลุ่มแบบขยาย/ย่อขั้นสูงสำหรับ portable paths, ไฟล์คอนฟิก JSON, ค่าโดเมนเริ่มต้น, สิทธิ์การใช้งาน, พฤติกรรม tray และ diagnostics
- มีการปรับ accessibility เพิ่มข้อความช่วยสำหรับ screen reader, ชื่อ automation ตามบริบท, access keys และ live notifications ทั่วทั้ง shell
- ปรับ responsive ให้ layout ของ dashboard, diagnostics, settings, services และ shell toolbar เรียงตัวได้เหมาะกับหน้าต่างขนาดแคบ
- มีโครงสร้าง workspace แบบพกพาใน `usr/`, `www/`, `bin/`, `data/` และ `temp/`
- มีโครง Named-pipe IPC ระหว่าง UI และ supervisor
- มี supervisor ส่วนที่ทำงานกับ runtime จริง พร้อม managed services ที่ขับเคลื่อนด้วยคอนฟิกใน `usr/config/services.json`
- Supervisor สามารถ resolve alias `current` หรือโฟลเดอร์ `bin/<service>/<version>` ที่ตรงกัน ไปยัง runtime เวอร์ชันล่าสุดที่สอดคล้องกับช่วงเวอร์ชันในคอนฟิก และแสดงคำใบ้ของเวอร์ชันที่มีอยู่เมื่อหาเวอร์ชันที่ตรงไม่ได้
- ระบบจัดการแพ็กเกจมี source registry แบบอิงไฟล์และ flow การติดตั้งครบวงจร โดยโหลด manifest ที่มากับโปรเจกต์จาก `package-manifests/`, เก็บนิยามแหล่งแพ็กเกจใน `usr/config/sources.json`, บันทึกสถานะ lock ของเวอร์ชันที่ร้องขอใน `usr/config/packages.lock.json`, นำ artifact ไปพักไว้ที่ `usr/cache/packages`, ตรวจสอบค่า SHA-256 ของแคชเมื่อ manifest ระบุไว้, แตก archive ไปยัง `bin/<package>/<version>`, รีเฟรช `bin/<package>/current`, รองรับการติดตั้งหรืออัปเดตแพ็กเกจที่เลือกใช้งานแบบคลิกเดียวรวมถึงการลบโฟลเดอร์แพ็กเกจที่ติดตั้งแล้ว, persist สถานะ `InstalledPackages` และแสดงสุขภาพของ catalog/cache ใน Settings และรายงาน diagnostics
- Settings มีหน้าสำหรับสลับเวอร์ชัน runtime ของแพ็กเกจ PHP, Node.js, Python และ Java ที่อิง manifest; เมื่อสลับไปยังเวอร์ชันที่ติดตั้งแล้ว ระบบจะรีเฟรช alias ที่ใช้งานทันที ส่วนเวอร์ชันที่ยังไม่มีจะถูกตั้งเป็นเป้าหมายการติดตั้งครั้งถัดไป
- Settings ยังแสดงรายการ tool packages ที่อิง manifest เช่น Mailpit และ Composer พร้อมคำสั่งที่มีให้ สถานะการเลือก/การติดตั้ง และความพร้อมของ bin ที่ active สำหรับ terminal ของโปรเจกต์
- Diagnostics ตรวจสอบความเข้ากันได้ระหว่าง package และ service เพื่อให้ service ที่คอนฟิกไว้ เวอร์ชันที่ร้องขอ แพ็กเกจที่ถูกเลือกใช้งาน และพาธ executable จาก manifest สอดคล้องกันก่อนที่ profile หรือการเริ่ม service จะอาศัยข้อมูลเหล่านี้
- Stack profiles ถูกเก็บใน `usr/config/profiles.json`, แสดงใน Settings และ command palette, จำโปรไฟล์ที่ active, รวมการเลือกแพ็กเกจของโปรไฟล์เข้าไปใน `packages.lock.json`, ตรวจสอบ reference ของ service และทำให้คำสั่ง Start All ยึดตามชุด service ของโปรไฟล์ที่ active
- Supervisor สร้างคอนฟิกตั้งต้นสำหรับ `Nginx`, `Apache` และ `MariaDB` ภายใต้ `usr/config/nginx`, `usr/config/apache` และ `usr/config/mariadb`
- Supervisor สามารถ scaffold `PostgreSQL` แบบเลือกใช้ได้ พร้อม `postgresql.conf`, `pg_hba.conf`, รายละเอียดการเชื่อมต่อ และการเตรียมคลัสเตอร์ครั้งแรกผ่าน `initdb`
- Supervisor สามารถ scaffold services แบบเลือกใช้ได้สำหรับ `Redis`, `Memcached` และ `Mailpit`, สร้าง `redis.conf` และเขียนรายละเอียดการเชื่อมต่อไว้ใต้ `usr/config/memcached` และ `usr/config/mailpit`
- หน้า Services รองรับการสั่ง start/stop ราย service ผ่าน supervisor
- หน้า Services สามารถเปิดเครื่องมือจัดการฐานข้อมูลแบบเดสก์ท็อปที่ตรวจพบสำหรับ MariaDB และ PostgreSQL, คัดลอก connection snippet ภายในเครื่อง และเปิดไฟล์รายละเอียดการเชื่อมต่อหรือโฟลเดอร์คอนฟิกที่ถูกสร้างไว้
- Supervisor ค้นหาโปรเจกต์ภายใต้ `www/`, สร้าง local URL, รองรับการ override โดเมนต่อโปรเจกต์ผ่าน `.locora.json` พร้อมคำอธิบายหรือแท็กบน dashboard, ข้าม hostname ที่ชนกันอย่างปลอดภัย, เขียน vhost ของ Nginx และ Apache และสร้างตัวอย่าง hosts file ที่ปลอดภัย
- การ์ดโปรเจกต์บน Dashboard สามารถปักหมุดขึ้นด้านบนของรายการ และเปิด local URL, โฟลเดอร์, terminal และ preferred editor ได้โดยตรง
- การ์ดโปรเจกต์บน Dashboard รองรับเมนูคลิกขวาและ action ผ่าน command palette สำหรับเปิดเว็บ/โฟลเดอร์, เปิดโฟลเดอร์โปรเจกต์ใน File Explorer, คัดลอก path ของโฟลเดอร์, เปิด terminal/editor และปักหมุดโปรเจกต์
- Settings สามารถสร้างไฟล์สำหรับลงทะเบียนเมนูคลิกขวาใน Windows Explorer ของผู้ใช้ปัจจุบันไว้ใต้ `usr/shell` โดยมีไฟล์ `.reg` สำหรับติดตั้ง/ถอนการติดตั้งเมนูของ Locora ทั้งระดับโฟลเดอร์และพื้นหลัง รวมถึง command relay ที่ส่งต่อคำสั่งจาก Explorer ไปยังแอปที่กำลังรันอยู่ได้เมื่อทำได้
- การเปิด preferred editor ตอนนี้จะเลือก target ที่เหมาะกับโปรเจกต์ที่สุด (`.code-workspace`, `.slnx` / `.sln`, `*proj` เดี่ยว แล้วค่อยเป็นโฟลเดอร์), ลองค้นหา editor ยอดนิยมในเครื่อง เช่น VS Code, Cursor, Windsurf, VSCodium, Visual Studio และ JetBrains IDEs และรองรับ template คำสั่ง `PreferredEditor` แบบกำหนดเองใน `usr/config/appsettings.json`
- การเปิด project terminal ตอนนี้จะ inject metadata ของโปรเจกต์ในกลุ่ม `LOCORA_*`, ค่า `APP_URL`, ตัวแปร runtime เฉพาะสายงาน เช่น `PHP_BINARY`, `PYTHONHOME` และ `JAVA_HOME` เมื่อมี, prepend พาธ runtime ที่ active เช่น `bin/php/current`, `bin/nodejs/current`, `bin/python/current` หรือ `bin/java/current` และเพิ่ม `usr/aliases` รวมถึง `LOCORA_ALIASES_ROOT` / `LOCORA_TERMINAL_COMMANDS_FILE` สำหรับ shell shim และ preset แบบกำหนดเอง
- Tool packages ที่ติดตั้งแล้วและมี binary แบบ active หรือแตกไฟล์พร้อมใช้งาน จะถูก prepend เข้า `PATH` ของ project terminal ด้วย พร้อมตัวแปรแวดล้อม `LOCORA_TOOL_*` ที่บอกรากโฟลเดอร์ เวอร์ชัน และพาธคำสั่งของแต่ละเครื่องมือ
- หน้า Terminal สามารถเริ่ม session ที่ใช้ ConPTY บน Windows ได้โดยตรง, fallback ไปใช้ redirected shell process เมื่อ ConPTY ใช้ไม่ได้, เปิด session ระดับโปรเจกต์จาก project card พร้อม environment ชุดเดียวกัน, จัดการหลาย session ผ่านแท็บที่ปิดได้ และโหลด quick commands จาก `usr/config/terminal-commands.json` พร้อมปุ่มเข้าถึงโฟลเดอร์ aliases แบบคลิกเดียว
- หน้า Domains & Hosts สามารถสร้าง local CA, ออกใบรับรอง HTTPS รายโปรเจกต์, trust หรือ untrust CurrentUser root store และรายงานสถานะความน่าเชื่อถือได้
- หน้า Domains & Hosts และ Diagnostics สามารถซ่อม local SSL ได้ด้วยการสร้างใบรับรองโปรเจกต์ที่หาย หมดอายุ ไม่ถูกต้อง หรือเก่าจนล้าสมัยใหม่อีกครั้ง พร้อม regenerate artifact ที่เกี่ยวข้องกับ HTTPS
- หน้า Domains & Hosts สามารถนำ preview ของ hosts ที่สร้างขึ้นไปใช้งานเป็น managed block ได้ และย้อนกลับจาก backup ได้
- หน้า Domains & Hosts สามารถแก้ไข suffix ของโดเมนที่สร้างและ scheme เริ่มต้นใน `usr/config/projects.json` จากนั้น regenerate hosts และ artifact ของ vhost ได้ในขั้นตอนเดียว
- หน้า Domains & Hosts และ Diagnostics มี action สำหรับซ่อมแซมโดยเฉพาะ เพื่อ regenerate hosts preview รวมถึง artifact ของ vhost สำหรับ Nginx และ Apache
- หน้า Domains & Hosts สามารถขอให้ `Locora.Supervisor` รีสตาร์ตแบบ UAC ได้เมื่อการเขียน hosts ต้องใช้สิทธิ์ผู้ดูแลระบบ
- Settings และ Domains & Hosts แสดง diagnostics เกี่ยวกับสิทธิ์และระดับการยกระดับสำหรับการเขียน workspace, automation ของ hosts, ความพร้อมในการรีสตาร์ตผ่าน UAC และการเข้าถึง SSL trust ของ CurrentUser
- Supervisor ตรวจสอบพอร์ตของ service ที่คอนฟิกไว้ การกำหนดพอร์ตซ้ำกันระหว่าง service และ external TCP listener พร้อม hint ของ PID/โปรเซสบน Windows
- หน้า Services และ Domains & Hosts มี action ซ่อมแบบคลิกเดียวที่ regenerate คอนฟิก runtime ทั่วไป, vhosts, hosts preview และ artifact ราย service ก่อนตรวจสอบ Nginx อีกครั้ง
- Settings สามารถคัดลอกหรือส่งออกรายงาน diagnostics แบบ Markdown ที่ครอบคลุม services, health issues, ผลการตรวจสอบความถูกต้อง, การวิเคราะห์พอร์ต, สิทธิ์, diagnostics ของใบรับรอง SSL, โปรเจกต์ และพาธสำคัญ
- หน้า Diagnostics รวม health issues, การตรวจสอบคอนฟิก, ความขัดแย้งของพอร์ต, การตรวจสอบสิทธิ์, สถานะ local SSL, action ซ่อมด่วน และการส่งออกรายงานไว้ในที่เดียว
- Locora ทำงานพร้อมไอคอนจริงบน Windows tray, ซ่อนตัวไปยัง notification area เมื่อต้องการปิดหน้าต่าง และมี tray actions สำหรับเปิดกลับมา, diagnostics, refresh, start all, stop all และ exit
- หน้า Domains & Hosts สามารถตรวจสอบคอนฟิก Nginx ที่สร้างขึ้นได้ และ supervisor จะบล็อกการเริ่ม Nginx หากการตรวจสอบไม่ผ่าน
- Managed services จะรีสตาร์ตอัตโนมัติหลังปิดตัวโดยไม่คาดคิด ด้วยกลไก backoff, จำนวนครั้งสูงสุด และการระงับเมื่อ crash-loop ซึ่งจะแสดงใน health issues
- Supervisor snapshot รายงานสถานะ admin/elevated และ process id แล้ว และขั้นตอนเริ่มต้นของ supervisor ใช้ mutex แยกตาม root เพื่อลดการชนกันของ pipe
- เมื่อมีการเปิดแอปหรือ supervisor ซ้ำ ระบบจะแสดงคำเตือนให้ผู้ใช้เห็น แทนการปิดตัวเองแบบเงียบ ๆ

## การเปิดโปรเจกต์บน Windows

1. ติดตั้ง Visual Studio 2022 พร้อมการรองรับ WinUI 3 / Windows App SDK
2. เปิดไฟล์ `Locora.sln`
3. รัน `Locora.Supervisor` ก่อน แล้วจึงรัน `Locora.App`
4. ตั้งค่า `LOCORA_ROOT` ใน debug profile หากต้องการให้ portable root ชี้มาที่โฟลเดอร์รีโปนี้ระหว่างการดีบัก
