/*
AutoDubbing - Nhập dữ liệu demo vào SQL Server Docker.
Nguồn: demo-content.export.sql (SHA-256: 17fe2a868079f290de5d3920f777570156d57d3c3ef82369a3ccfacc6bccaebc).
Dữ liệu: 8 POI Published, 19 bản dịch Ready, 26 audio Ready.

CÁCH CHẠY:
1. Mở file trên kết nối SSMS localhost,11433 (SQL Server Linux).
2. @DemoAdminId = 1 là Admin Active vừa được tạo trong database Docker.
   Trên máy khác, tạo Admin trước và điền đúng ID tài khoản kiểm thử.
3. Chạy TOÀN BỘ file bằng F5, không chọn chạy riêng các INSERT.
4. Kiểm tra thông báo PASS và số lượng trả về 8 / 19 / 26.

Script chỉ nhập vào POI/BAN_DICH/AUDIO đang trống; có dữ liệu thì dừng.
Không tạo/sửa tài khoản, bảng, migration; không gọi Gemini hoặc Supabase.
Audio được tham chiếu bằng URL đã xuất; không sao chép file âm thanh.
Giữ nguyên ID, văn bản, URL, trạng thái và thời điểm từ file nguồn.
Chỉ thay liên kết Admin của POI bằng @DemoAdminId.
*/
USE [AutoDubbingDb];

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DemoAdminId int = 1;
DECLARE @IdentityTable sysname = NULL;

IF DB_NAME() <> N'AutoDubbingDb'
    THROW 51001, N'Sai database. Chỉ chạy trên AutoDubbingDb của Docker.', 1;

IF @@VERSION NOT LIKE N'%Linux%'
    THROW 51002, N'Sai server. Hãy kết nối SQL Server Docker localhost,11433 (Linux).', 1;

IF @@TRANCOUNT <> 0
    THROW 51003, N'Cửa sổ query đang có transaction. Hãy dùng cửa sổ query mới.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.NGUOI_DUNG WITH (UPDLOCK, HOLDLOCK)
        WHERE id = @DemoAdminId AND trang_thai = N'Active'
    )
        THROW 51004, N'Không tìm thấy Admin Active đúng @DemoAdminId trong database đích.', 1;

    IF EXISTS (SELECT 1 FROM dbo.POI WITH (TABLOCKX, HOLDLOCK))
       OR EXISTS (SELECT 1 FROM dbo.BAN_DICH WITH (TABLOCKX, HOLDLOCK))
       OR EXISTS (SELECT 1 FROM dbo.AUDIO WITH (TABLOCKX, HOLDLOCK))
        THROW 51005, N'POI/BAN_DICH/AUDIO đã có dữ liệu. Script dừng, không ghi đè hoặc xóa dữ liệu.', 1;

    SET IDENTITY_INSERT [dbo].[POI] ON;
    SET @IdentityTable = N'POI';
    INSERT [dbo].[POI] ([id], [nguoi_dung_id], [ten], [lat], [lng], [ban_kinh_kich_hoat], [anh_thumbnail], [mo_ta_goc], [ngon_ngu_goc], [trang_thai]) VALUES (1, @DemoAdminId, N'Nhà thờ Đức Bà', 10.7798, 106.699, 50, NULL, N'Chào mừng bạn đến với Nhà thờ Đức Bà Sài Gòn, biểu tượng tôn giáo và kiến trúc di sản của thành phố. Được khánh thành vào năm 1880, công trình này mang đậm phong cách kiến trúc Roman pha trộn Gothic. Điều đặc biệt là toàn bộ vật liệu xây dựng, từ những viên gạch đỏ au cho đến ngói, đều được nhập khẩu trực tiếp từ Pháp. Dù nằm giữa trung tâm đô thị nhộn nhịp, kiến trúc uy nghi cùng hai tháp chuông vươn cao của nhà thờ vẫn luôn mang lại cảm giác bình yên và cổ kính cho bất cứ ai ghé thăm.', N'vi', N'Published');
    INSERT [dbo].[POI] ([id], [nguoi_dung_id], [ten], [lat], [lng], [ban_kinh_kich_hoat], [anh_thumbnail], [mo_ta_goc], [ngon_ngu_goc], [trang_thai]) VALUES (2, @DemoAdminId, N'Bưu điện Trung tâm Sài Gòn', 10.78, 106.6999, 50, NULL, N'Ngay bên cạnh Nhà thờ Đức Bà là Bưu điện Trung tâm Sài Gòn, một kiệt tác kiến trúc được xây dựng vào cuối thế kỷ 19. Tòa nhà mang vóc dáng của một nhà ga châu Âu với những vòm mái rộng và các ô cửa sổ thanh lịch. Bước vào bên trong, bạn sẽ được chiêm ngưỡng hai tấm bản đồ lịch sử khổng lồ vẽ trên vòm trần và cảm nhận nhịp sống hối hả nhưng hoài cổ. Bưu điện hiện vẫn hoạt động bình thường, là nơi bạn có thể dừng chân, viết và gửi đi những tấm bưu thiếp xinh xắn đến khắp nơi trên thế giới.', N'vi', N'Published');
    INSERT [dbo].[POI] ([id], [nguoi_dung_id], [ten], [lat], [lng], [ban_kinh_kich_hoat], [anh_thumbnail], [mo_ta_goc], [ngon_ngu_goc], [trang_thai]) VALUES (3, @DemoAdminId, N'Dinh Độc Lập', 10.777, 106.6953, 150, NULL, N'Xin giới thiệu đến bạn Dinh Độc Lập, công trình in đậm dấu ấn lịch sử hiện đại của Việt Nam. Được thiết kế bởi kiến trúc sư tài ba Ngô Viết Thụ, công trình là sự kết hợp hài hòa giữa nghệ thuật phương Tây hiện đại và triết lý phương Đông truyền thống. Đây là nơi chứng kiến khoảnh khắc lịch sử vào trưa ngày 30 tháng 4 năm 1975, đánh dấu thời khắc thống nhất đất nước. Ngày nay, mọi không gian, phòng ốc và vật dụng bên trong Dinh vẫn được bảo tồn nguyên vẹn như một bảo tàng của thời gian.', N'vi', N'Published');
    INSERT [dbo].[POI] ([id], [nguoi_dung_id], [ten], [lat], [lng], [ban_kinh_kich_hoat], [anh_thumbnail], [mo_ta_goc], [ngon_ngu_goc], [trang_thai]) VALUES (4, @DemoAdminId, N'Bảo tàng Chứng tích Chiến tranh', 10.7795, 106.692, 60, NULL, N'Bạn đang có mặt tại Bảo tàng Chứng tích Chiến tranh, một trong những bảo tàng thu hút đông du khách quốc tế nhất tại Việt Nam. Không gian nơi đây lưu giữ những hình ảnh và hiện vật chân thực về sự tàn khốc của chiến tranh. Qua các khu trưng bày tĩnh lặng và đầy xúc động, bảo tàng không chỉ kể lại một quá khứ đau thương từ góc nhìn của người Việt Nam, mà còn truyền tải thông điệp mạnh mẽ về tình yêu hòa bình và hy vọng cho tương lai.', N'vi', N'Published');
    INSERT [dbo].[POI] ([id], [nguoi_dung_id], [ten], [lat], [lng], [ban_kinh_kich_hoat], [anh_thumbnail], [mo_ta_goc], [ngon_ngu_goc], [trang_thai]) VALUES (5, @DemoAdminId, N'Chợ Bến Thành', 10.7725, 106.698, 80, NULL, N'Hãy hòa mình vào nhịp sống sôi động tại Chợ Bến Thành, ngôi chợ mang tính biểu tượng của Sài Gòn hoạt động từ năm 1914. Nổi bật với tháp đồng hồ bốn mặt ở cửa Nam, chợ Bến Thành là một thiên đường mua sắm và ẩm thực thực sự. Từ các món ăn đường phố hấp dẫn, ly cà phê pha phin đậm đà, đến những món đồ thủ công mỹ nghệ và lụa là vải vóc, bạn có thể tìm thấy mọi thứ và trải nghiệm văn hóa thương mại đặc trưng nhất của người dân địa phương.', N'vi', N'Published');
    INSERT [dbo].[POI] ([id], [nguoi_dung_id], [ten], [lat], [lng], [ban_kinh_kich_hoat], [anh_thumbnail], [mo_ta_goc], [ngon_ngu_goc], [trang_thai]) VALUES (6, @DemoAdminId, N'Nhà hát Thành phố', 10.7767, 106.7032, 50, NULL, N'Trước mắt bạn là Nhà hát Thành phố, một công trình văn hóa sang trọng mang phong cách kiến trúc Flamboyant đặc trưng của Pháp. Từng chi tiết phù điêu, hoa văn và bức tượng trước cổng đều được chạm khắc vô cùng tinh xảo. Từ khi khánh thành vào năm 1900 đến nay, đây luôn là trung tâm nghệ thuật bậc nhất của Sài Gòn, nơi thường xuyên sáng đèn đón khán giả đến thưởng thức các buổi hòa nhạc cổ điển, múa ballet và các show diễn nghệ thuật đương đại mang đậm bản sắc Việt.', N'vi', N'Published');
    INSERT [dbo].[POI] ([id], [nguoi_dung_id], [ten], [lat], [lng], [ban_kinh_kich_hoat], [anh_thumbnail], [mo_ta_goc], [ngon_ngu_goc], [trang_thai]) VALUES (7, @DemoAdminId, N'Phố đi bộ Nguyễn Huệ', 10.7741, 106.7036, 100, NULL, N'Chào mừng bạn đến với Phố đi bộ Nguyễn Huệ, nhịp đập sôi động của Sài Gòn hiện đại. Quảng trường rộng lớn này được lát đá granite, trải dài từ trụ sở Ủy ban Nhân dân Thành phố rực rỡ xuống tận bến Bạch Đằng lộng gió. Dọc hai bên đường là sự đan xen thú vị giữa các tòa nhà chọc trời hiện đại và những chung cư cũ cải tạo thành chuỗi quán cà phê độc đáo. Vào buổi tối, con phố trở thành một không gian văn hóa mở với ánh sáng rực rỡ, đài phun nước và các nghệ sĩ biểu diễn đường phố.', N'vi', N'Published');
    INSERT [dbo].[POI] ([id], [nguoi_dung_id], [ten], [lat], [lng], [ban_kinh_kich_hoat], [anh_thumbnail], [mo_ta_goc], [ngon_ngu_goc], [trang_thai]) VALUES (8, @DemoAdminId, N'Thảo Cầm Viên Sài Gòn', 10.7875, 106.705, 150, NULL, N'Bước qua cổng Thảo Cầm Viên Sài Gòn, bạn đang tiến vào lá phổi xanh tĩnh lặng hiếm hoi giữa lòng đô thị. Thường được gọi thân thương là Sở Thú, đây là một trong những vườn thú và công viên thực vật lâu đời nhất trên thế giới, được thành lập từ năm 1865. Dưới những tán cây cổ thụ khổng lồ hàng trăm năm tuổi, bạn có thể tản bộ trên những con đường rợp bóng mát, tìm hiểu về hàng ngàn loài động, thực vật quý hiếm và tận hưởng một buổi chiều bình yên, xa rời khói bụi thành phố.', N'vi', N'Published');
    SET IDENTITY_INSERT [dbo].[POI] OFF;
    SET @IdentityTable = NULL;
    SET IDENTITY_INSERT [dbo].[AUDIO] ON;
    SET @IdentityTable = N'AUDIO';
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1, 1, N'vi', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/vi/a091bc7c5029df494de2ac60dbac63d676bb438ff1851b06ac5787e7383f7496.wav', 33, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (2, 1, N'en', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/en/2390eb6b7322243b986afd07caa45a45935c1c0225f5b14e059b70234138a207.wav', 38, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (3, 1, N'ja', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ja/fe8928e74157b5712003c57bfa6238e2181d0cdf0e73486428cb4e04f6fdb0f4.wav', 41, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (4, 1, N'ko', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ko/f5182a7e0966b7962ca47a46f1d147419445e782b8f2df36ecd011b14ccaa1c5.wav', 41, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1002, 1, N'zh-hans', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/zh-hans/5f063aaa02071ea7f45a41518a3f812c62de1d32274c9f72754fe2be6ca3424d.wav', 34, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1003, 1, N'fr', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/fr/e2ed3cd6a870457c6f3117a3df7b975210ef4df40af4c79ec456536034916ecd.wav', 37, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1004, 1, N'de', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/de/90c9e36efe67f122172f122fbcef8a25bc0de2972dcb7d3d3ec090b46fdc3b64.wav', 40, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1005, 1, N'es', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/es/f678a38f66d183b1fd92bc67a8e15314fd561f843c6d4e45a82b6db665aefe6f.wav', 41, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1006, 1, N'ru', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ru/ead043cd5451d5b80bca1595df9544ac6cb4e9eea8a88c25c98a887c58c4c450.wav', 38, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1007, 1, N'it', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/it/ac7983350bfb2217ee4ee3706d381a8aef56ef4e3fd930434b82760c7bcd9fdc.wav', 40, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1008, 1, N'pt', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/pt/66e2db65c5920345e7787d187718d0a663af297477f2a2ce62404037b29e6f6e.wav', 41, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1009, 1, N'th', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/th/7273025f01624da17df02664c4b17e4c4eec2d808bdc74b618c0f94509f4e153.wav', 44, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1010, 1, N'id', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/id/f1b51f02e6b1ddc19f0360175976b5883cc5c6a1b10570a875f9f7628c399947.wav', 39, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1011, 1, N'ms', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/ms/a9813c64a6315b5b48c5e5d251913c0aec30d325cdba95fadecbdf43c9e8860a.wav', 39, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1012, 1, N'hi', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-1/hi/ab55e1942c672469783d1ea3ab1071f61cbf53e73f425b7afd879f9909de137a.wav', 36, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1013, 8, N'vi', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-8/vi/393dfb5bc910ece5c199e5ecb699dce73b291013f90781d41e69882e570630f2.wav', 35, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1014, 8, N'en', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-8/en/5b4c0f62f49eb2da709034620637760ac3f5cc1659f7f0ed6a25c48658480206.wav', 35, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1015, 7, N'vi', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-7/vi/c309775cc82c746a251d25687bcd9494386ff1553f136c5bd4f7ffd831fc2c03.wav', 31, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1016, 6, N'vi', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-6/vi/f49f40590885fa80fd6de1b2164c82a21d475189f318542850e17de0ea5f1c9d.wav', 31, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1017, 7, N'ja', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-7/ja/a194470f3efd3ef68d714d635fea1832218a104f6909036ea3296d38c61ca61d.wav', 39, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1018, 5, N'vi', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-5/vi/a066f5322c569e475e954c9b21723ceb8a3b31a06b486492b441ae91ee0679e8.wav', 29, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1019, 4, N'vi', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-4/vi/478aa176bcd3507e97002d0d501bdbff78cc79c40c019b703a97e29a22732d0b.wav', 27, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1020, 4, N'ja', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-4/ja/85dc5ad53c7555c302d47f4da7ad83a71ce8f96224c4adce94203c2999ec69b4.wav', 34, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1021, 3, N'vi', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-3/vi/7a70140288a540f0cd9385664d783259308c8e5181150f8dc3be82ce83645345.wav', 34, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1022, 3, N'ja', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-3/ja/5d2ca4544b918199c312e02984b4044fa39f19fcbcd9b64583d2f6bfdb761391.wav', 41, N'Ready');
    INSERT [dbo].[AUDIO] ([id], [poi_id], [ngon_ngu], [duong_dan_file], [thoi_luong], [trang_thai]) VALUES (1023, 5, N'en', N'https://ktnefjccvscpioqbyhus.supabase.co/storage/v1/object/public/poi-audio/generated/poi-5/en/5fe7f5c334c89eafc8786d80c7c5725c8662bf20e480c992f34f444026cc5ea0.wav', 31, N'Ready');
    SET IDENTITY_INSERT [dbo].[AUDIO] OFF;
    SET @IdentityTable = NULL;
    SET IDENTITY_INSERT [dbo].[BAN_DICH] ON;
    SET @IdentityTable = N'BAN_DICH';
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1, 1, N'en', N'Welcome to Nhà thờ Đức Bà Sài Gòn, a religious and architectural heritage symbol of the city. Inaugurated in 1880, this structure features a Roman architectural style blended with Gothic. What is special is that all construction materials, from the bright red bricks to the tiles, were imported directly from France. Despite being located in the middle of a bustling urban center, the majestic architecture and the two soaring bell towers of the church always bring a sense of peace and antiquity to anyone who visits.', N'Ready', CAST(N'2026-10-19T23:23:41.9003106' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (2, 1, N'zh-hans', N'欢迎来到西贡 Nhà thờ Đức Bà，这是这座城市的宗教象征和建筑遗产。该建筑于1880年落成，融合了罗马式和哥特式建筑风格。特别之处在于，所有的建筑材料，从红砖到瓦片，均直接从法国进口。尽管坐落在繁华的都市中心，但教堂威严的建筑和高耸的双钟楼，始终为每一位访客带来宁静与古朴的感觉。', N'Ready', CAST(N'2026-10-19T23:35:40.4118782' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (3, 1, N'ja', N'Nhà thờ Đức Bà Sài Gònへようこそ。ここは当市の宗教的象徴であり、建築遺産でもあります。1880年に落成したこの建物は、ロマネスク様式にゴシック様式が融合した建築スタイルが特徴です。特筆すべきは、真っ赤なレンガから屋根瓦に至るまで、すべての建築資材がフランスから直接輸入されたという点です。賑やかな都市の中心部にありながら、その威厳ある建築と高くそびえる2つの鐘楼は、訪れるすべての人に常に安らぎと古き良き趣を感じさせてくれます。', N'Ready', CAST(N'2026-10-19T23:36:30.3274289' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (4, 1, N'ko', N'사이공 Nhà thờ Đức Bà에 오신 것을 환영합니다. 이곳은 도시의 종교적 상징이자 건축 유산입니다. 1880년에 완공된 이 건축물은 로마네스크 양식과 고딕 양식이 혼합된 건축 스타일을 띠고 있습니다. 특히 붉은 벽돌부터 기와에 이르기까지 모든 건축 자재를 프랑스에서 직접 수입했다는 점이 특징입니다. 번화한 도심 한복판에 위치해 있음에도 불구하고, 웅장한 건축미와 높게 솟은 두 개의 종탑은 이곳을 찾는 모든 이들에게 평온함과 고풍스러운 느낌을 선사합니다.', N'Ready', CAST(N'2026-10-19T23:36:50.5747168' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1002, 1, N'fr', N'Bienvenue à Nhà thờ Đức Bà Sài Gòn, symbole religieux et patrimoine architectural de la ville. Inauguré en 1880, cet édifice est imprégné d''un style architectural roman mêlé au gothique. Ce qui est particulier, c''est que l''ensemble des matériaux de construction, des briques rouge vif aux tuiles, a été importé directement de France. Bien qu''il soit situé au cœur d''une zone urbaine animée, l''architecture majestueuse et les deux clochers élancés de l''église procurent toujours un sentiment de paix et d''ancienneté à tous ceux qui la visitent.', N'Ready', CAST(N'2026-10-23T10:39:15.7876264' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1003, 1, N'de', N'Willkommen in der Nhà thờ Đức Bà Sài Gòn, einem religiösen Symbol und architektonischen Erbe der Stadt. Das 1880 eingeweihte Bauwerk ist stark vom romanischen Architekturstil geprägt, der mit gotischen Elementen vermischt ist. Das Besondere daran ist, dass sämtliche Baumaterialien, von den tiefroten Ziegeln bis hin zu den Dachziegeln, direkt aus Frankreich importiert wurden. Obwohl sie inmitten des geschäftigen Stadtzentrums liegt, vermitteln die majestätische Architektur und die beiden hoch aufragenden Glockentürme der Kirche jedem Besucher stets ein Gefühl von Frieden und Altertümlichkeit.', N'Ready', CAST(N'2026-10-23T11:15:15.9101416' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1004, 1, N'es', N'Bienvenido a Nhà thờ Đức Bà Sài Gòn, símbolo religioso y patrimonio arquitectónico de la ciudad. Inaugurada en 1880, esta obra posee un marcado estilo arquitectónico románico mezclado con gótico. Lo especial es que todos los materiales de construcción, desde los ladrillos rojos hasta las tejas, fueron importados directamente de Francia. A pesar de estar situada en el centro de una bulliciosa zona urbana, la majestuosa arquitectura y las dos torres campanario de la iglesia siempre brindan una sensación de paz y antigüedad a todo aquel que la visita.', N'Ready', CAST(N'2026-10-23T11:16:50.6386326' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1005, 1, N'ru', N'Добро пожаловать в Nhà thờ Đức Bà Сайгона, религиозный символ и архитектурное наследие города. Открытое в 1880 году, это сооружение выполнено в архитектурном стиле, сочетающем романский стиль с готикой. Примечательно, что все строительные материалы, от ярко-красного кирпича до черепицы, были импортированы непосредственно из Франции. Несмотря на расположение в самом центре оживленного мегаполиса, величественная архитектура и две высокие колокольни церкви всегда дарят ощущение покоя и старины каждому посетителю.', N'Ready', CAST(N'2026-10-23T11:18:01.9401962' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1006, 1, N'it', N'Benvenuti alla Nhà thờ Đức Bà Sài Gòn, simbolo religioso e architettonico del patrimonio della città. Inaugurata nel 1880, questa struttura presenta un marcato stile architettonico romanico mescolato al gotico. La particolarità è che tutti i materiali da costruzione, dai mattoni rosso vivo alle tegole, sono stati importati direttamente dalla Francia. Nonostante si trovi nel centro di una vivace area urbana, l''architettura maestosa e le due alte torri campanarie della chiesa offrono sempre una sensazione di pace e antichità a chiunque la visiti.', N'Ready', CAST(N'2026-10-23T11:19:09.3782506' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1007, 1, N'pt', N'Bem-vindo à Nhà thờ Đức Bà Sài Gòn, símbolo religioso e patrimônio arquitetônico da cidade. Inaugurada em 1880, esta construção possui um estilo arquitetônico que mistura o românico com o gótico. O que é especial é que todo o material de construção, desde os tijolos vermelhos até as telhas, foi importado diretamente da França. Embora esteja localizada no centro de uma metrópole movimentada, a arquitetura majestosa e as duas torres sineiras da igreja sempre trazem uma sensação de paz e antiguidade a qualquer pessoa que a visite.', N'Ready', CAST(N'2026-10-23T11:20:14.8570411' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1008, 1, N'th', N'ยินดีต้อนรับสู่ Nhà thờ Đức Bà Sài Gòn สัญลักษณ์ทางศาสนาและสถาปัตยกรรมมรดกของเมือง อาคารแห่งนี้เปิดใช้งานในปี 1880 โดยมีรูปแบบสถาปัตยกรรมโรมาเนสก์ผสมผสานกับโกธิค สิ่งที่พิเศษคือวัสดุก่อสร้างทั้งหมด ตั้งแต่อิฐสีแดงสดไปจนถึงกระเบื้องมุงหลังคา ล้วนถูกนำเข้าโดยตรงจากประเทศฝรั่งเศส แม้จะตั้งอยู่ท่ามกลางใจกลางเมืองที่พลุกพล่าน แต่สถาปัตยกรรมอันสง่างามและหอระฆังคู่ที่สูงตระหง่านของโบสถ์ยังคงมอบความรู้สึกสงบและเก่าแก่ให้กับทุกคนที่มาเยือนเสมอ', N'Ready', CAST(N'2026-10-23T11:21:24.3694726' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1009, 1, N'id', N'Selamat datang di Nhà thờ Đức Bà Sài Gòn, simbol keagamaan dan warisan arsitektur kota ini. Diresmikan pada tahun 1880, bangunan ini kental dengan gaya arsitektur Roman yang dipadukan dengan gaya Gotik. Hal yang istimewa adalah seluruh bahan bangunan, mulai dari batu bata merah hingga genteng, diimpor langsung dari Prancis. Meskipun terletak di tengah pusat kota yang sibuk, arsitektur yang megah serta dua menara lonceng yang menjulang tinggi dari gereja ini selalu memberikan rasa damai dan kuno bagi siapa pun yang berkunjung.', N'Ready', CAST(N'2026-10-23T11:22:31.9838936' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1010, 1, N'ms', N'Selamat datang ke Nhà thờ Đức Bà Sài Gòn, simbol keagamaan dan seni bina warisan bandar raya ini. Dirasmikan pada tahun 1880, bangunan ini sarat dengan gaya seni bina Roman yang digabungkan dengan Gothic. Perkara yang istimewa ialah keseluruhan bahan binaan, daripada batu bata merah sehinggalah jubin, semuanya diimport terus dari Perancis. Walaupun terletak di tengah-tengah kawasan bandar yang sibuk, seni bina yang tersergam indah serta dua menara loceng yang menjulang tinggi tetap memberikan rasa tenang dan klasik kepada sesiapa sahaja yang berkunjung.', N'Ready', CAST(N'2026-10-23T11:23:31.7446506' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1011, 1, N'hi', N'Nhà thờ Đức Bà Sài Gòn में आपका स्वागत है, जो शहर का एक धार्मिक और स्थापत्य विरासत प्रतीक है। 1880 में उद्घाटन किया गया यह निर्माण रोमन और गॉथिक वास्तुकला शैली का मिश्रण है। विशेष बात यह है कि निर्माण सामग्री, लाल ईंटों से लेकर टाइलों तक, सब कुछ सीधे फ्रांस से आयात किया गया था। हलचल भरे शहरी केंद्र में स्थित होने के बावजूद, चर्च की भव्य वास्तुकला और दो ऊंचे घंटाघर किसी भी आगंतुक के लिए हमेशा शांति और प्राचीनता का अहसास कराते हैं।', N'Ready', CAST(N'2026-10-23T11:24:56.6969878' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1012, 8, N'en', N'Stepping through the gates of Thảo Cầm Viên Sài Gòn, you are entering a rare, tranquil green lung in the heart of the metropolis. Often affectionately called the Zoo, this is one of the oldest zoos and botanical gardens in the world, established in 1865. Under the canopies of giant ancient trees hundreds of years old, you can stroll along shaded paths, learn about thousands of rare animal and plant species, and enjoy a peaceful afternoon, far from the city''s dust and bustle.', N'Ready', CAST(N'2026-10-25T09:27:06.5461316' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1013, 7, N'ja', N'Phố đi bộ Nguyễn Huệへようこそ。ここは現代サイゴンの活気あふれる鼓動を感じられる場所です。この広大な広場は花崗岩で舗装されており、華やかな市人民委員会庁舎から風通しの良いバクダン埠頭まで続いています。通りの両側には、現代的な超高層ビルと、ユニークなカフェチェーンに改装された古いアパートメントが興味深く混在しています。夜になると、この通りは鮮やかな光、噴水、そしてストリートパフォーマーたちが集うオープンな文化空間へと姿を変えます。', N'Ready', CAST(N'2026-10-25T10:34:11.3251333' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1014, 4, N'ja', N'あなたは今、ベトナムで最も多くの外国人観光客を惹きつける博物館の一つである「Bảo tàng Chứng tích Chiến tranh」にいます。この場所は、戦争の残酷さを物語るリアルな写真や展示品を収蔵しています。静寂に包まれ、感動を呼ぶ展示エリアを通じて、この博物館はベトナム人の視点から痛ましい過去を伝えるだけでなく、平和への愛と未来への希望という力強いメッセージを発信しています。', N'Ready', CAST(N'2026-10-25T10:42:12.3612611' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1015, 3, N'ja', N'ベトナムの現代史に深く刻まれた建造物、Dinh Độc Lậpをご紹介します。優れた建築家であるNgô Viết Thụによって設計されたこの建物は、西洋の現代芸術と東洋の伝統的な哲学が調和した融合体です。ここは1975年4月30日の正午、国の統一を象徴する歴史的な瞬間を目撃した場所です。今日、Dinh Độc Lập内部のあらゆる空間、部屋、そして調度品は、時の博物館として当時のまま保存されています。', N'Ready', CAST(N'2026-10-25T13:09:17.5110564' AS DateTime2));
    INSERT [dbo].[BAN_DICH] ([id], [poi_id], [ngon_ngu], [van_ban], [trang_thai], [thoi_diem_cap_nhat]) VALUES (1016, 5, N'en', N'Immerse yourself in the vibrant rhythm of life at Chợ Bến Thành, the iconic market of Saigon that has been operating since 1914. Distinguished by its four-faced clock tower at the South gate, Chợ Bến Thành is a true shopping and culinary paradise. From enticing street food and rich phin-brewed coffee to handicrafts and silks, you can find everything and experience the most characteristic commercial culture of the local people.', N'Ready', CAST(N'2026-10-25T13:22:42.8365175' AS DateTime2));
    SET IDENTITY_INSERT [dbo].[BAN_DICH] OFF;
    SET @IdentityTable = NULL;

    IF (SELECT COUNT_BIG(*) FROM dbo.POI) <> 8
       OR (SELECT COUNT_BIG(*) FROM dbo.BAN_DICH) <> 19
       OR (SELECT COUNT_BIG(*) FROM dbo.AUDIO) <> 26
        THROW 51006, N'Số lượng sau nhập không khớp. Hủy toàn bộ lần nhập.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    IF @IdentityTable = N'POI'
        SET IDENTITY_INSERT dbo.POI OFF;
    IF @IdentityTable = N'AUDIO'
        SET IDENTITY_INSERT dbo.AUDIO OFF;
    IF @IdentityTable = N'BAN_DICH'
        SET IDENTITY_INSERT dbo.BAN_DICH OFF;

    THROW;
END CATCH;

PRINT N'PASS: Đã nhập đủ 8 POI, 19 bản dịch và 26 audio.';

SELECT N'POI' AS Bang, COUNT_BIG(*) AS SoBanGhi FROM dbo.POI
UNION ALL
SELECT N'BAN_DICH', COUNT_BIG(*) FROM dbo.BAN_DICH
UNION ALL
SELECT N'AUDIO', COUNT_BIG(*) FROM dbo.AUDIO;

SELECT id, ten_dang_nhap, ho_ten, trang_thai
FROM dbo.NGUOI_DUNG
WHERE id = @DemoAdminId;

SELECT
    p.id,
    p.ten,
    p.nguoi_dung_id,
    (SELECT COUNT(*) FROM dbo.BAN_DICH AS t WHERE t.poi_id = p.id) AS SoBanDich,
    (SELECT COUNT(*) FROM dbo.AUDIO AS a WHERE a.poi_id = p.id) AS SoAudio
FROM dbo.POI AS p
ORDER BY p.id;
