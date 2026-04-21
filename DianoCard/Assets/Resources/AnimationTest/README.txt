애니메이션 테스트 — 사진 넣는 법
================================================

폴더 구조:
  Assets/Resources/AnimationTest/
  ├── <캐릭터이름>/                  ← 폴더 하나가 캐릭터 하나
  │   ├── idle_f01.png
  │   ├── idle_f02.png
  │   ├── idle_f03.png
  │   ├── attack_f01.png
  │   ├── attack_f02.png
  │   └── ...
  └── <다른캐릭터>/ ...

파일명 규칙 (중요):
  {애니이름}_f{번호}.png

  - 애니이름: 영어 소문자 권장 (idle / attack / hit / cast / walk 등)
  - _f       : 구분자 리터럴 ("_f" 그대로)
  - 번호     : 2자리 0-padded 십진수 (01, 02, …, 08, 09, 10, 11)
  - 확장자   : .png (Unity가 자동으로 Sprite로 임포트)

좋은 예:
  idle_f01.png, idle_f02.png, idle_f03.png, idle_f04.png
  attack_f01.png ~ attack_f08.png

나쁜 예 (동작 안 함):
  idle_1.png           ← 번호 자릿수 맞아야 함 (f01이 정답. f1도 호환되지만 f01 권장)
  idle-01.png          ← 구분자가 _f 여야 함
  Idle_F01.png         ← 확장자만 신경쓰면 됨. 파일명 대소문자는 허용됨 (애니 이름은 내부에서 lowercase 정규화)
  idle_f01.psd         ← PNG만 지원 (Unity가 Sprite로 인식해야 함)

Unity 임포트 확인:
  PNG를 드롭하면 기본적으로 Sprite로 임포트됨.
  만약 리스트에 "Sprite가 없음" 경고가 뜨면:
    1) Unity에서 PNG 파일 선택
    2) Inspector → Texture Type → "Sprite (2D and UI)" 확인
    3) Apply

사용법:
  1) 위 구조대로 사진 넣기
  2) Unity 실행 → 로비 우측 상단 "🎬 애니 테스트" 클릭
  3) 왼쪽에서 캐릭터 → 애니 선택
  4) 단축키:
       Space  재생/정지
       ← / →  프레임 이동
       R      처음으로
       L      루프 토글
       F5     폴더 재스캔 (사진 추가 후)
       Esc    로비로

프레임 수 제한:
  1 ~ 99개까지. (2자리 번호라서)
  현재 프로젝트는 4~8 프레임 기준으로 디자인됨.
