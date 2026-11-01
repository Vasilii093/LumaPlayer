# Объединённые цепочки RIFE — 0.3.3.4 exp

Один граф `engine/gpu/models/rife-v4.6_ensembleFalse_residual` используется для RIFE 4.6 в режимах 720p, 900p и 1080p. Веса совпадают с оригиналом. `fuse-residual-model.py` воспроизводит граф из `flownet.original.param`; не преобразовывать уже оптимизированный файл повторно.

## Обработчики

| Режим | Фильтр | DLL | Настройки выполнения |
|---|---|---|---|
| 720p | interpolate-rife-resident.vpy | rife-native/librife-native.dll | Прежний путь передачи, 2 GPU workers, VS cache 256 МиБ |
| 900p | interpolate-rife-throughput.vpy | rife-memory/librife-native.dll | Прежний mapped-memory путь, 3 GPU workers, VS cache 1024 МиБ |
| 1080p | interpolate-rife-resident.vpy | rife-memory/librife-native.dll | Путь и DLL из 0.3.3.3 exp, 2 GPU workers, VS cache 1024 МиБ |

В обоих нативных проектах `RIFE/fused_residual.h` содержит одинаковый слой `LumaResidual`, зарегистрированный как `luma.Residual`. В нём сохранены оба промежуточных FP16-округления, арифметика остаётся FP32. Поддерживаемый профиль: Vulkan buffer storage, pack4, FP16 storage. Это не поддержка старых экспериментальных флагов FP16 arithmetic или pack8.

## Сборка 720p

Компилятор: clang++ из llvm-mingw-20261020-ucrt-x86_64. ncnn — закреплённый проектом commit `305837fd4a722ebc47c5d72e72d8ec9ae970e932`, без экспериментальной правки Concat. Локальная зависимость для проверки: `work/framecache/ncnn`; она не изменялась.

Новый отдельный build directory создаётся командой CMake с исходниками `source/native-rife`, генератором Ninja и аргументами `-DCMAKE_BUILD_TYPE=Release -DNCNN_BENCHMARK=OFF -DNCNN_SOURCE=<ncnn> -DVS_INCLUDE=<app>/engine/runtime/Lib/site-packages/vapoursynth/include`, а также путями `CMAKE_C_COMPILER` и `CMAKE_CXX_COMPILER` из toolchain. После сборки копируется только полученный `librife-native.dll` в `engine/gpu/rife-native/`.

DLL `rife-memory` перенесена без изменений из 0.3.3.3 exp. Она уже содержит новый слой; для 900p изменён только выбор графа. Сборка этой DLL использует прежнюю отдельную зависимость ncnn с оптимизацией Concat. Не подменять ею дерево зависимости 720p при воспроизведении сравнительных замеров.

Компилятор и CMake не входят в переносимый комплект плеера. Исходный `build-native.ps1` предполагает внешний runtime сборки в `engine/runtime-trt`; при другом расположении запускайте CMake с указанными выше аргументами.
