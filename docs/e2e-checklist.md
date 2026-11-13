# Checklist kiểm thử E2E (thủ công, trên máy ảo sạch)

Chuẩn bị: chạy `powershell -ExecutionPolicy Bypass -File build\build-portable.ps1 -Out <thư mục> -InitVault -WithDemo` (lần đầu cần mạng để tải bộ cài demo; kho có 9 phần mềm và bộ "Văn phòng cơ bản"). Ghi lại mã kho bằng `tools\SecureInstaller.Packager.exe fingerprint .`. Mỗi mục tick sau khi đạt.

1. **Chạy từ USB thật**
   - [ ] Làm: chép thư mục `SecureInstaller` vào USB, cắm vào máy khác, chạy `SecureInstaller.exe` (chấp nhận UAC), đăng nhập.
   - Kết quả mong đợi: app mở, quét và hiện báo cáo, danh sách phần mềm hiện đúng; không cần cài thêm gì.

2. **Copy thư mục ra `C:\` rồi chạy**
   - [ ] Làm: chép nguyên thư mục ra `C:\SecureInstaller` (và thử thêm đường dẫn có dấu cách/tiếng Việt như `C:\Dự án 1`), chạy lại.
   - Kết quả mong đợi: đăng nhập được bằng cùng mật khẩu, không bị ràng buộc phần cứng.

3. **Mở `.pkg.enc` bằng 7-Zip**
   - [ ] Làm: nhấp phải `vault\packages\*.pkg.enc` và mở bằng 7-Zip.
   - Kết quả mong đợi: 7-Zip báo không mở được tệp; không thấy tên tệp bên trong.

4. **Sửa 1 byte trong catalog**
   - [ ] Làm: dùng trình sửa hex đổi 1 byte của `vault\catalog.json.enc` (hoặc `catalog.sig`), chạy app.
   - Kết quả mong đợi: app báo "Kho bị sửa đổi, không thể sử dụng." và không cho cài.

5. **Cài `.msi` 7-Zip trên máy ảo sạch**
   - [ ] Làm: chọn 7-Zip, bấm "Cài đặt".
   - Kết quả mong đợi: thanh tiến độ chạy 0 → 100%, báo thành công, 7-Zip xuất hiện trong Programs and Features.

6. **Cài `.exe` ngầm, tự nhận diện loại bộ cài**
   - [ ] Làm: cài lần lượt Notepad++ (NSIS), Git (Inno) và .NET Desktop Runtime (Burn).
   - Kết quả mong đợi: không hiện cửa sổ cài đặt, kết quả thành công, trạng thái chuyển thành "Đã cài" (kể cả khi Windows ghi thêm số build, ví dụ 8.0.34.36622).

7. **Bước sau cài chép file**
   - [ ] Làm: cài "Phần mềm thử nghiệm" (`demo-notepad`, đăng nhập `nguoidung` hoặc admin).
   - Kết quả mong đợi: `%ProgramData%\SecureInstallerDemo\readme.txt` chứa "SecureInstaller demo" và `config.ini` được chép đến cùng thư mục. Trước khi cài nó hiện "Chưa cài".

8. **Lab KMS loopback**
   - [ ] Làm: trên máy ảo, **chỉ** chạy `slmgr /skms 127.0.0.2` (không dùng bất kỳ tool crack nào), chạy lại quét.
   - Kết quả mong đợi: báo cáo ghi "Có dấu hiệu crack" kèm bằng chứng "KMS host = 127.0.0.2". Khôi phục bằng `slmgr /ckms`.

9. **Máy không cài Office**
   - [ ] Làm: quét trên máy không có Office.
   - Kết quả mong đợi: mục Office hiện "Không cài", không phải "Nghi vấn" hay "Có dấu hiệu crack".

10. **Rút USB khi đang cài**
    - [ ] Làm: bắt đầu cài một phần mềm lớn (Git) từ USB, rút USB giữa chừng.
    - Kết quả mong đợi: bộ cài chạy tiếp đến hết; log được ghi ở `%ProgramData%\SecureInstaller\logs` và đồng bộ lại vào USB lần đăng nhập ghi được kế tiếp.

11. **Chạy từ ổ chỉ đọc**
    - [ ] Làm: tạo ISO chứa thư mục `SecureInstaller`, mount và chạy từ đó.
    - Kết quả mong đợi: đăng nhập và cài bình thường; log nằm ở `%ProgramData%`; không có lỗi ghi vào kho.

12. **Không còn rác trong thư mục tạm**
    - [ ] Làm: sau mọi lần cài (thành công, thất bại, hủy), chạy `dir %ProgramData%\SecureInstaller\work`.
    - Kết quả mong đợi: thư mục `work` trống.

13. **Dò Internet**
    - [ ] Làm: rút cáp mạng/tắt NIC rồi quét; sau đó cắm lại và quét.
    - Kết quả mong đợi: lúc mất mạng báo cáo ghi "Không"; có mạng ghi "Có".

14. **Tiến độ MSI thật**
    - [ ] Làm: cài MSI thật (7-Zip), quan sát màn hình tiến độ.
    - Kết quả mong đợi: hiện tên các bước (ACTIONSTART) và thanh tiến độ chỉ được quay về đầu tối đa một lần.

15. **ACL thư mục tạm khi chạy quyền quản trị**
    - [ ] Làm: trong lúc cài, chạy `icacls %ProgramData%\SecureInstaller\work\*` ở một cửa sổ khác.
    - Kết quả mong đợi: chỉ có Administrators và SYSTEM (kế thừa bị tắt).

16. **Đóng cửa sổ khi đang cài**
    - [ ] Làm: bấm nút đóng (X) hoặc Alt+F4 khi đang cài.
    - Kết quả mong đợi: cửa sổ không đóng được cho đến khi cài xong.

17. **Mã kho trong trang Quản trị**
    - [ ] Làm: so mã kho trong trang Quản trị với kết quả `tools\SecureInstaller.Packager.exe fingerprint .`; màn hình đăng nhập không còn hiện mã kho.
    - Kết quả mong đợi: hai mã giống nhau; chép thư mục sang ổ khác thì mã không đổi.

18. **Khóa KMS trên máy không thuộc domain**
    - [ ] Làm: quét trên máy LTSC/kích hoạt bằng KMS (GVLK) không join domain.
    - Kết quả mong đợi: báo "Nghi vấn" (hành vi dự kiến trên máy lab); không báo "Có dấu hiệu crack" nếu không có thêm bằng chứng.

19. **Điền sẵn trong form "Thêm phần mềm"**
    - [ ] Làm: Quản trị → Phần mềm → "Thêm phần mềm" → "Chọn file cài…" và chọn lần lượt một file .msi và một file .exe thật.
    - Kết quả mong đợi: tên, phiên bản được điền sẵn; hiện "Đã nhận diện: …" đúng loại bộ cài; file không bị chạy.

20. **Không đổi tên được thư mục tạm khi đang cài**
    - [ ] Làm: trong lúc cài, thử đổi tên thư mục con trong `%ProgramData%\SecureInstaller\work` bằng tài khoản không phải quản trị.
    - Kết quả mong đợi: báo Access denied.

21. **Thư mục đích nằm dưới junction**
    - [ ] Làm: tạo `mklink /J C:\Lien C:\Windows\Temp`, đặt thư mục đích của một phần mềm zip là `C:\Lien\App`, cài.
    - Kết quả mong đợi: cài thất bại với thông báo đường dẫn không an toàn; không có file nào được ghi.

22. **Gỡ cài đặt thật**
    - [ ] Làm: bấm "Gỡ" lần lượt với 7-Zip (MSI), Notepad++ (NSIS), Git (Inno), .NET Desktop Runtime (Burn) và "Script thử nghiệm".
    - Kết quả mong đợi: gỡ ngầm, không cần viết script; sau khi gỡ, trạng thái về "Chưa cài" và Programs and Features không còn phần mềm đó.

23. **Hàng đợi 3 phần mềm và Hủy**
    - [ ] Làm: tick 3 phần mềm, bấm "Cài các mục đã chọn", bấm Hủy khi phần mềm thứ hai đang giải mã.
    - Kết quả mong đợi: phần mềm đang chạy dừng sớm, các mục còn lại báo đã hủy; thư mục `%ProgramData%\SecureInstaller\work` trống.

24. **Bộ cài đặt**
    - [ ] Làm: tab "Bộ cài đặt" → mở danh sách bộ "Văn phòng cơ bản" → "Cài đặt".
    - Kết quả mong đợi: chỉ các mục ghi "✓ Sẽ được cài" được cài; mục đã cài hiện lý do bỏ qua.

25. **Biểu tượng phần mềm**
    - [ ] Làm: xem danh sách phần mềm demo.
    - Kết quả mong đợi: phần mềm có biểu tượng lấy từ bộ cài; phần mềm không có biểu tượng hiện hình mặc định.

26. **Dung lượng bản build**
    - [ ] Làm: chạy script build.
    - Kết quả mong đợi: dòng "Dung lượng chương trình (không tính kho)" ≤ 120 MB.

27. **Mở lại báo cáo quét**
    - [ ] Làm: Quản trị → Lịch sử quét, mở một lần quét cũ; trên màn hình chính bấm ô "Báo cáo quét".
    - Kết quả mong đợi: báo cáo hiện ở chế độ chỉ xem, "Quay lại" về đúng màn hình trước, phần mềm đã tick vẫn giữ.

28. **Quét file từ khóa**
    - [ ] Làm: Quản trị → Cấu hình quét, tick Desktop, Downloads, Tất cả ổ dữ liệu; đặt một file có từ khóa trên Desktop và một file trong thư mục OneDrive chỉ lưu trên mạng (online-only); quét lại.
    - Kết quả mong đợi: cả hai file đều có trong tab "File từ khóa"; nút "Mở thư mục" mở Explorer và chọn sẵn file.

29. **Sửa phần mềm không cần chọn lại file**
    - [ ] Làm: Quản trị → Sửa một phần mềm, chỉ đổi tên rồi lưu; sau đó sửa lại và "Thêm thư mục" đi kèm.
    - Kết quả mong đợi: lần đầu lưu ngay, không bắt chọn lại file cài; lần hai cài thử thấy thư mục đi kèm nằm cạnh file cài.

Dọn dẹp sau khi thử: xóa `%ProgramData%\SecureInstallerDemo`, và xóa tài khoản thử trước khi phát hành: `tools\SecureInstaller.Packager.exe remove-user . nguoidung --user admin`.
