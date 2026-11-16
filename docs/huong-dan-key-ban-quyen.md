# Thêm phần mềm cần key bản quyền

SecureInstaller hỗ trợ đưa **key bản quyền và công cụ kích hoạt hợp lệ của nhà phát hành** vào kho. Chọn một trong các cách dưới, tùy phần mềm.

> **Chỉ dùng key và công cụ kích hoạt hợp lệ** do nhà phát hành cấp cho đơn vị. Không đưa keygen hay công cụ crack vào kho: chúng vi phạm bản quyền, là nguồn mã độc phổ biến, và chính SecureInstaller có phần quét phát hiện dấu hiệu crack.

## Cách 1 — Key nhập sẵn khi cài ngầm (gọn nhất)

Nhiều bộ cài nhận key qua tham số dòng lệnh. Ghi key vào ô **"Tham số cài ngầm"** (phần Nâng cao của form phần mềm). App sẽ cài và kích hoạt trong một bước, không cần thao tác tay.

Ví dụ tham số thường gặp (xem tài liệu của nhà phát hành):

- MSI: `PIDKEY=XXXXX-XXXXX-XXXXX-XXXXX-XXXXX` hoặc `SERIALNUMBER=...`
- EXE: `/key=...`, `/serial=...`

## Cách 2 — File license đi kèm

Dùng khi nhà phát hành cấp một **file license** (`.lic`, `.key`, `.dat`…).

- **Chung cho mọi máy:** thêm file đó vào mục **"File đi kèm"** của phần mềm. Khi cài, file nằm cạnh bộ cài; nếu phần mềm cần file ở một thư mục cố định, dùng bước **post_action copy** hoặc Cách 3.
- **Mỗi đơn vị một file license khác nhau:** dùng **"Chép dữ liệu theo đơn vị"** (xem phần Nâng cao). Đặt file theo đơn vị trong `DuLieu\<mã phần mềm>\` và khai báo, ví dụ `{DonVi}.lic => license.lic`. Máy của đơn vị nào chỉ nhận file license của đơn vị đó.

## Cách 3 — Công cụ kích hoạt chính thức chạy sau khi cài

Dùng khi phần mềm phải **mở công cụ kích hoạt của nhà phát hành** sau khi cài (ví dụ kích hoạt offline: phần mềm sinh mã yêu cầu, bạn lấy mã kích hoạt từ nhà phát hành rồi nhập vào).

1. Thêm công cụ của nhà phát hành vào **"File đi kèm"**.
2. Viết một script `.cmd` ngắn, chọn ở ô **"Script sau cài"**, để chép công cụ ra thư mục cố định rồi mở nó:

   ```bat
   @echo off
   set "DEST=%ProgramData%\KichHoat"
   if not exist "%DEST%" mkdir "%DEST%"
   copy /Y "..\installer\CongCuKichHoat.exe" "%DEST%\" >nul
   start "" "%DEST%\CongCuKichHoat.exe"
   exit /b 0
   ```

   (`..\installer\` là nơi chứa file đi kèm; script chạy trong thư mục `scripts`.)

3. Sau khi cài xong, công cụ hiện lên để bạn nhập/kích hoạt theo quy trình của nhà phát hành. Bước nhập mã này vẫn làm tay; app không tự động được vì mã do nhà phát hành cấp cho từng máy.

> Nên xóa công cụ kích hoạt khỏi máy (ví dụ `C:\ProgramData\KichHoat`) sau khi kích hoạt xong.
