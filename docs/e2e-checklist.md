# Checklist kiểm thử E2E (thủ công, trên máy ảo sạch)

Chuẩn bị: chạy `build\build-portable.ps1 -Out <thư mục> -InitVault -WithDemo`, thêm gói 7-Zip MSI và một gói `.exe` cài ngầm bằng Packager. Ghi lại mã kho bằng `tools\SecureInstaller.Packager.exe fingerprint .`. Mỗi mục tick sau khi đạt.

1. **Chạy từ USB thật**
   - [ ] Làm: chép thư mục `SecureInstaller` vào USB, cắm vào máy khác, chạy `SecureInstaller.exe` (chấp nhận UAC), đăng nhập.
   - Kết quả mong đợi: app mở, quét và hiện báo cáo, danh sách gói hiện đúng; không cần cài thêm gì.

2. **Copy thư mục ra `C:\` rồi chạy**
   - [ ] Làm: chép nguyên thư mục ra `C:\SecureInstaller` (và thử thêm đường dẫn có dấu cách/tiếng Việt như `C:\Dự án 1`), chạy lại.
   - Kết quả mong đợi: đăng nhập được bằng cùng mật khẩu, không bị ràng buộc phần cứng.

3. **Mở `.pkg.enc` bằng 7-Zip**
   - [ ] Làm: nhấp phải `vault\packages\*.pkg.enc` và mở bằng 7-Zip.
   - Kết quả mong đợi: 7-Zip báo không mở được tệp; không thấy tên tệp bên trong.

4. **Sửa 1 byte trong catalog**
   - [ ] Làm: dùng trình sửa hex đổi 1 byte của `vault\catalog.json.enc` (hoặc `catalog.sig`), chạy app.
   - Kết quả mong đợi: app báo `Strings.VaultTampered` và không cho cài.

5. **Cài `.msi` 7-Zip trên máy ảo sạch**
   - [ ] Làm: chọn gói 7-Zip, bấm cài.
   - Kết quả mong đợi: thanh tiến độ chạy 0 → 100%, báo thành công, 7-Zip xuất hiện trong Programs and Features.

6. **Cài một `.exe` ngầm**
   - [ ] Làm: cài gói loại `exe` có `silent_args` (ví dụ `/S`).
   - Kết quả mong đợi: không hiện cửa sổ cài đặt, kết quả thành công, phần mềm có mặt trên máy.

7. **Bước sau cài chép file**
   - [ ] Làm: cài gói `demo-notepad` (đăng nhập `nguoidung` hoặc admin).
   - Kết quả mong đợi: `%ProgramData%\SecureInstallerDemo\readme.txt` chứa "SecureInstaller demo" và `config.ini` được chép đến cùng thư mục. Gói demo trước khi cài hiện "Chưa cài".

8. **Lab KMS loopback**
   - [ ] Làm: trên máy ảo, **chỉ** chạy `slmgr /skms 127.0.0.2` (không dùng bất kỳ tool crack nào), chạy lại quét.
   - Kết quả mong đợi: báo cáo ghi "Có dấu hiệu crack" kèm bằng chứng (máy chủ KMS 127.0.0.2). Khôi phục bằng `slmgr /ckms`.

9. **Máy không cài Office**
   - [ ] Làm: quét trên máy không có Office.
   - Kết quả mong đợi: mục Office hiện "Không cài", không phải "Nghi vấn" hay "Có dấu hiệu crack".

10. **Rút USB khi đang cài**
    - [ ] Làm: bắt đầu cài một gói lớn từ USB, rút USB giữa chừng.
    - Kết quả mong đợi: bộ cài chạy tiếp đến hết; log được ghi ở `%ProgramData%\SecureInstaller\logs` và đồng bộ lại vào USB lần đăng nhập ghi được kế tiếp.

11. **Chạy từ ổ chỉ đọc**
    - [ ] Làm: tạo ISO chứa thư mục `SecureInstaller`, mount và chạy từ đó.
    - Kết quả mong đợi: đăng nhập và cài bình thường; log nằm ở `%ProgramData%`; không có lỗi ghi vào kho.

12. **Không còn rác trong `%TEMP%`**
    - [ ] Làm: sau mọi lần cài (thành công, thất bại, hủy), chạy `dir %TEMP%\SI_*`.
    - Kết quả mong đợi: không còn thư mục `SI_*` nào.

13. **Dò Internet**
    - [ ] Làm: rút cáp mạng/tắt NIC rồi quét; sau đó cắm lại và quét.
    - Kết quả mong đợi: lúc mất mạng báo cáo ghi "Không"; có mạng ghi "Có".

14. **Tiến độ MSI thật**
    - [ ] Làm: cài MSI thật (7-Zip), quan sát màn hình tiến độ.
    - Kết quả mong đợi: hiện tên các bước (ACTIONSTART) và thanh tiến độ chỉ được quay về đầu tối đa một lần.

15. **ACL thư mục tạm khi chạy quyền quản trị**
    - [ ] Làm: trong lúc cài, chạy `icacls %TEMP%\SI_*` ở một cửa sổ khác.
    - Kết quả mong đợi: chỉ có Administrators và SYSTEM (kế thừa bị tắt).

16. **Đóng cửa sổ khi đang cài**
    - [ ] Làm: bấm nút đóng (X) hoặc Alt+F4 khi đang cài.
    - Kết quả mong đợi: cửa sổ không đóng được cho đến khi cài xong.

17. **Mã kho trên màn hình đăng nhập**
    - [ ] Làm: so mã kho ở màn hình đăng nhập với kết quả `tools\SecureInstaller.Packager.exe fingerprint .`.
    - Kết quả mong đợi: hai mã giống nhau; sau khi thay `vault.meta` bằng kho khác thì mã khác đi.

18. **Khóa KMS trên máy không thuộc domain**
    - [ ] Làm: quét trên máy LTSC/kích hoạt bằng KMS (GVLK) không join domain.
    - Kết quả mong đợi: báo "Nghi vấn" (hành vi dự kiến trên máy lab); không báo "Có dấu hiệu crack" nếu không có thêm bằng chứng.

19. **MSI prefill trong form "Thêm phần mềm"**
    - [ ] Làm: Quản trị → Gói → "Thêm phần mềm" → "Chọn file cài…" và chọn một file .msi thật.
    - Kết quả mong đợi: tên, phiên bản và mã sản phẩm MSI (ProductCode) được điền sẵn; loại hiện "MSI"; file không bị chạy.

Dọn dẹp sau khi thử: xóa `%ProgramData%\SecureInstallerDemo`, và xóa tài khoản thử trước khi phát hành: `tools\SecureInstaller.Packager.exe remove-user . nguoidung --user admin`.
