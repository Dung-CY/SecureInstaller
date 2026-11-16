# Yêu cầu tương thích Windows

SecureInstaller phải chạy trên **mọi bản Windows 10 và Windows 11**, gồm mọi phiên bản (version) và mọi ấn bản (edition): Home, Pro, Enterprise, Education, LTSC/LTSB.

## Hiện trạng (đã đạt)

- Đóng gói **self-contained, single-file** (`net8.0-windows`, `win-x64`): máy đích **không cần cài .NET**. Chạy được trên Win10/Win11 mà không phụ thuộc ấn bản.
- Chỉ dùng các API và lớp WMI có sẵn trên mọi ấn bản Win10 trở lên: `Win32_BIOS`, `Win32_BaseBoard`, `Win32_ComputerSystem`, `Win32_DiskDrive`, `Win32_NetworkAdapter`, `Win32_PnPEntity`, registry HKLM, Windows Installer (msi.dll), `SetComputerNameEx`, cfgmgr32 (ngày thiết bị).
- Mọi nguồn dữ liệu Windows nằm sau interface + có fake test, nên kiểm thử không phụ thuộc máy thật.

## Giới hạn đã biết (ghi rõ để không hiểu nhầm "mọi Windows")

- **Kiến trúc:** bản build là **x64**. Không chạy trên Windows 32-bit (x86). Trên Windows ARM64, chạy qua giả lập x64 (Win11 ARM). Hầu hết máy văn phòng là x64 nên chấp nhận; nếu cần x86 phải build thêm RID `win-x86`.
- **Phiên bản tối thiểu:** .NET 8 yêu cầu Windows 10 **1607 (build 14393)** trở lên. Các bản Win10 rất cũ (1507, 1511) không được .NET 8 hỗ trợ.
- Các mục này phải nêu trong README/trang phát hành, không coi là lỗi.

## Quy tắc cho tính năng phụ thuộc ấn bản hoặc phần cứng

Những tính năng không có trên mọi máy **phải giảm nhẹ êm (graceful degradation)**, không được làm app lỗi:

- **Tắt BitLocker:** `manage-bde`/WMI mã hóa ổ chỉ có trên Pro/Enterprise/Education/LTSC. Trên Windows **Home** không có; app phải báo "Bản Windows này không hỗ trợ quản lý BitLocker" và đi tiếp, không được crash.
- **Cảnh báo Secure Boot:** máy BIOS cũ (Legacy/CSM, không UEFI) không có khóa registry Secure Boot. Khi không đọc được, coi là "không rõ" và **không** cảnh báo nhầm.
- Mọi lệnh gọi phần cứng/registry đặc thù phải bọc try/catch và trả trạng thái "không rõ"/"không hỗ trợ" thay vì ném lỗi.

## Kiểm thử

- Lý tưởng: chạy `docs/e2e-checklist.md` trên máy ảo mỗi ấn bản (Win10 Pro, Win10 LTSC, Win11 Pro, Win11 Home).
- Tối thiểu: Win10 và Win11, một bản Home (để xác nhận phần BitLocker giảm nhẹ đúng) và một bản Pro/LTSC.
