---
license: apache-2.0
library_name: PaddleOCR
language:
- en
- zh
pipeline_tag: image-to-text
tags:
- OCR
- PaddlePaddle
- PaddleOCR
- text_recognition
---

<div align="center">


<h1 align="center">

PP-OCRv6: From 1.5M to 34.5M Parameters, Surpassing Billion-Scale VLMs on OCR Tasks

</h1>

[![repo](https://img.shields.io/github/stars/PaddlePaddle/PaddleOCR?color=ccf)](https://github.com/PaddlePaddle/PaddleOCR)
[![HuggingFace](https://img.shields.io/badge/HuggingFace-black.svg?logo=data:image/png;base64,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&labelColor=white)](https://huggingface.co/PaddlePaddle/PP-OCRv6_medium_rec)
[![X](https://img.shields.io/badge/X-PaddlePaddle-6080F0)](https://x.com/PaddlePaddle)
[![License](https://img.shields.io/badge/license-Apache_2.0-green)](./LICENSE)
[![Safetensors Model](https://img.shields.io/badge/Safetensors_Model-black.svg?logo=data:image/png;base64,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&labelColor=white)](https://huggingface.co/PaddlePaddle/PP-OCRv6_medium_rec_safetensors)
[![ONNX Model](https://img.shields.io/badge/ONNX_Model-333333.svg?logo=data%3Aimage%2Fpng%3Bbase64%2CiVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAaUklEQVR42t17e3xU1bn2s9a%2BzN5zycwk5G64eEHggIkECGo0YkGpFVBhhApWq8XaImpFP22pUrSnR9FWj7ZW0Gqtes5XxnqoSglIEgY1hEu4aOWeQggJuZNkLnvPvqz1%2FcFMDLeQBPqd7zvr98svv%2FySzOz1rGc97%2Fs%2B7zvAhV%2F0HD%2F%2Fj14EAMaPzy%2BYOHHinQUFBaMAYMmSJfT%2F6Qe%2BECsQCAjBYNAuLCx8UBSFVwGAc24zZt2zbduO9wKBgNDc3EwAICMjgweDQQaA%2F08BgADgY8aM8cuyVEsp9TDG4oIgODhHU2amd8Lq1WVHOD99vyUlJeJ%2FJygXFICioqJM27b2AfBSSjlAoGkxYllWWJKk%2FZIkfSlJ8nZZlnelpaXtW7VqVfN%2FNygXCgAKgF9xxRVOWZZqKKWZuq5zXddIUdFE7vP5yOHDh9He3o5oNArDMACgUxTFg30EhZSUlAg9QOElJSU0IyODNzc3k1AoZA8UpAsCQElJiRgKhazCwsLfyrK0QNM0e9iwYfTqq68hN910E1TVyaPRCO%2Fo6GANDfU4dOiwUFd3hDQ0NKCtrW2goJyRhf%2FXAeghfjcLAl1t2zazLAu%2F%2BtW%2Fsfz8fLGlpQUAhyCIEAQRkiSCUgrGGNc0jR8%2FfpzV19fj8OHeQRFF6UtZlrcLgrBT1%2FUjAKYLAr2ZcxyyLOtfd%2BzY0TAQEMgFoD7Gjx%2Fv55x9JUlSOuecKYoiLlv2Anc4HALnHISceBvOefcXIQSUUoii1CdQWltbEQ6HAQCyLOuiKCoAQCmFYRg73W7PVaFQKJ54rj6DIJ4n9WkoFLIALLcsK3vSpBuiVVWbnLm5ubbX6xU0TevePAAQQk76GQBM04BhxJOgEEopSU%2FPoDk5OSgqKoJlWVzTNB6LxVhbWxu2bNlMysvLlXg8bhFCiG3bTBSFglis60oAm5KMHGjW1u97X1RUdI9hxGeOHj0mNnz4cKWhoYGMHDmKC4JIGGPnpmCCCYIggFLaDUo4HEZnZyc458TtdhPbtunevXvEnTt3Cp2dnaCUigAEQohk28ywLLQAQDAY7NcVGCgDaCgUsidMmDDMssxXVFU1A4GAuHXrVoFzzi%2B77DLCmH3aaZ9rcc7BGIMoinC5XDBNk%2B%2Fdu4eXl5fTyspKUldXFx88eHDtd75zc1NZWfllADIty2KSJFKHg2YCOBgIBEgwGPynAkC%2BoT77o2manpkzZ8X8fr9z3759PDU1leXl5RHTNPsMQJIpsixDURR0dXWxjRs3ory8jFZXV5NwOHz8iiuu%2BMdTTz0dmTFjRjaAgk2bNrXed9%2B9fPz48cbhw4fVcDj8PIDi%2Fm5GHAD1hQT1n9R17bqxY8fGxo8fr7a2tvL6%2BqNk8OAh3O%2F303g8fk4AkhtXVRWiKPLGxmP8iy8q6YYNFXT37t2glB6dPHly7YIFDyI%2FP38wgMIkUzwetycajWLa9Bnyju3btVWrPrymuLj41mAwuKo%2FOiAOIORZEydOHBuP68%2F6%2Ff74tGnTJc45aWtr421tbSgpKeGSJFFN0yAIwllpLggC3G43bNvmNTU1fMOGDfTzzz8jtbW1Zlpa2qH58%2Bcf%2B8EP5rsyMzNHAkgFAMuywBjjsiyT%2FfsPcM45fD6fMHv2bLpu3VqmabFfFRYWrh41Kmj3NSSK%2FQ2ZU6dOdbS3t73DORdmzpxlulwuB%2BccDQ0NME2DDx9%2BOekZ%2Bk7deJLmsViMbd68GeXlZXTLli2kvb29a%2Fjwy%2F%2FxwgsvdgQCgXRJkiYAUBMb5wmhJMnnaGioJ6qqcqdT5UOGDnVMnfrt2H%2F914cj%2FX7fPUuX4o2kSF8wAEpKSoRgMGgVFRW9qOv66JKSktiIESOc4XAYHo8Hhw8fgsvl5kOGDDnp%2FidprigKJElCa2srW79%2BPamoqKBfffUlLMtqLC6%2B5tCCBQutq6%2B%2B%2BiIA%2BQCIbdsAwARBIImNn7Tq6o4iJSUFPp8f0WgUgTsC4oYNFXYsFltSUlLyn6FQKNYXFoj9oX5RUdEUw4g%2Fkpubq994400OTdMgiiJ0Xee1tbXIzc3l6enpxDRNMMZAKYXL5QIAfuTIERYKbRA2btxIa2pqbLfbXXvHHYGjDzzwY8eQIUMuAZCRpDkhhFNKCQB6agqcDJX19UeRlpYGt9uNWCyG7Oxc%2Bbbbbo%2B%2B9dYfchXFfBjAv%2FaFBX0BgCSKD180GnmLUmrPnDkLkiQJmqZBlmW0tbWhqamJXH%2F9JC7LMtU0DSkpKYjH42zXrl0oLy%2BjVVVVQlNTY2zIkCE1Tz%2F9dOvcufNSnU7nWADus9H8TPpBKYVlWWhsbERubi5kWSamaSIWi%2BKWadPkNWvWmK2tLY9PmTLljU8%2F%2FbQlkeuwASdCiSqM6br2O8MwLpoy5cb40KFDFV3XAQBOpxMtLS2IRqM8Pz8fKSleYhgGKytbz5Yu%2FQVdvPhn9OOPP27Nyxu85e23%2F7i1srJKmT%2F%2F%2FmudTmc%2BY8xtWRZnjHFBEAjpY9zs7OxEW1sbsrKzIYgiOOcwTRN%2Bf6o0e%2FbsuGma3nA4vBgADwQCZMAM6KH6d%2Bq6duell16qlZSUKLFYDIQQyLKMhoZ6VFZWErfbzUVR5O%2B99y5KS9fQAwcOQJblIzfddOORBQsepCNHjhoKIAcAEvebU0rPeL97S5QAdNcFOdk53VQRBAHRaASTp0xRPvror8aRI0d%2BOGnSpFeCweA%2FemNBbwDQYDDIJkyYcJFpmr9TFMW8%2FfaZAgBq2zYcDgcOHTqEt99%2BC6Zpwul0ktde%2B53Q1NSopaUNqn3ooYWN3%2F%2F%2BfZ7U1NTRAHxJmidSXzKQQiwJQGPjMZimicysLDDGugXXsiykpHjFuXd9L%2FLLZ5a6o9HoLwF8NxAI0LNlh7S3QueEgvK3TNPwTZs2zczMzJSTBY7L5cLmzVVM0zQoigrOOTdNE4899r9qtm2r5osWPX5VampqIefc15Pmic2f1zp6tB6CIPD09PQkm7pZEImEUXzNNeroMWN0XYvNLi4uLgwGg3YgEBD6DEBSPSdOnPCIYcSn5OcXxAoLx6nhcBiqqkJVVVRVVWHv3r1UlmUwZkMURWKaJr366qtHC4IwUtd1B0vEwP7c776suro6qKoKv98P2z655uCcQxRF4a67vmcTQkg8Hv%2B3%2FlaDNBQKWddcc82%2FGIb5XEqK15g2bZqUqMpw7NgxvPnmG%2Fj971%2BD3%2B83fT6fbds2IpGIoet69Nlnn0VDQ72tKAoYYxfUDk9u9OjROvj9fqSkpJzEgGSYjEQiGDt2rFo08aqYrmtTiouLJ5%2BNBac%2BIAkEAqSkpEQ0DONPlmU5brvtdjs7O1sKhyP4%2BOOP8MILy7B3717r3nvvjS9fvoLMnDkL7e3tbN68u2reeedPO%2F%2F%2B96%2Fq586dK3z99ddcFMXTHvC83JfuHKAeg9LToarqaQzowQQ6b948SJLEdV17bskS0FGjRvGzAhAIBISpU6fKwWDQ1nX9GV3Xxk6adEOsoKBAqaiowEsv%2FRqffPIJKy4ujv%2F%2B96%2Bzu%2B%2F%2BvizLshiLxQRKKabPmHF5cXHxNaWl61rb248fnDdvLgmFQj1AOD9jN5lea5qG5uZmZGdlcUmSyJm8QkopotEoLh8xQr3hW9%2BK6bpeWFZ21R1Lly5lp7KA9kh27NLS0viECROuNYz4kxdffLE%2BcuRI%2BeWXXyJvvvkG93q9xrJly8ynnnpaysnJkbu6Oolt22hsPAa32w1VdTJNj%2FPLLrs0PxTaaKWmpn193333kmBwJURRBGMcfTA2e4MAANDe3o6Ojg7k5OSCUnrW16SUwjRNMmfOdwWXy8U0Lf7s1KlTHQnDhPQEgADg48ePvamoaNz9jNnviKJIbNsWX331FbG2ttZ66KGHjVdeeYUWFU10RCIRqut6YlMMLS2t8HhSuMfjoYQQ0tHZxX0%2B74i169a5rrrq6u2PPPIwe%2FXVVyEIAieEoC8u0ZkZcOJ7c3NzIvXN7hXQJFuGDBmi3HLLLZqu65eGwx3zAbCpU6fKycOnAHhhYeFvBUEqBehySukw27ZRU1NDJ02aFF%2B%2BfAWfM2eOg3OI4XAYlFJQSkEIgWEYaGtrxaBBadzpdMK2bUiSRDq7wpwQOvTdd9%2FN%2Fd737tn27LPP6IsXLyaWZXFBEAYEQnKzDQ0NYIzxzKzMk3KAs7EgFovh9pmzpNTUNCsa1X4%2Bffp0T2lpaTyRGBFh%2FPjxBYTgD4wxxjm3bdsmLpeLL178c2v27DmSw%2BEQo9HoSSLEOYcgCIjH4wgGV2Lo0GHs1ttuo0bcIMlEJx6PcxDivummG91uT8rXv%2F71i56DBw8o1157HXc6ncS27e7X6ysAlFJUVJRj8%2BbNuHPuPHi9XnI2EUyywLIspKamCgKlZlVVldc0jam5udn5F12Usb%2B%2BvrGNMsZ8if%2B3KaVCPB4nOTm5ZOLEiXJnZyc1DAOCIJz2JidSzyjC4TAyszJBgZMESRAEYhgGj2m690cPPJD%2Fxz%2B%2Bs2%2FNmjVNd999N6mtPTzgCHHkSB08Hg%2B8Xi%2BxLKtPodO2bWRkZjoIIRzAlYIg%2Fsi2hfVFRUWZVJKkzYzxzbIsSwAIpZRfd911LGlpnSXEgFKKrq4uaLEYsrNz%2BFkoSBhjPBKNKjfffHPh6tVr6vfu3VM7d%2B5csmPH9n6BkGTL0aN1SEtL4y6XC72dfk%2FbjRCKjRtDsCyLcM6ZYRiGINDBlmXdQquqqjRJkqdblvkcY6zW4XDw0aNHn9HVOZUBx48fh2lZPDs7u7cTIABBVzgiFBTkj63YEAqbprnvrrvuImvXre0GoTdBSwJu2zaOHTuGjIxMKIpyzqhi2zZUVQUHx%2FH2di6eqBz5Ce0jANBKAZBNmzY1b9my7aeKoj6gaRoNhTbgRIrLen2gtrY2EEKQkZFBTmz27DQUBAGdXWGem5MzuqIiRIcOHfblD%2B%2B%2Fn7z77p8giiLvKXRnW11dXWhrbUN2TjYEQSC9PR9jDF6vF01NTXji8cexa9cuoqoqp5QKsiyLlmX9BcDfEgUPaGFhoTRq1KgyQRAOfPHFF%2FT48eNMFHv3S1pbW%2BBwOHha2iDYjJ%2BzwBNFkXR2hbnqVC%2F75JNPUqdO%2Ffa2xx9%2F3HrhhWWEUsoT7bFey%2BCucBdyc3LPys5EJILb7cbq1Z%2Fg%2Fvk%2FwM6dO7jL5WKcc8IYW2aa1s3V1dvvqK6uNpMyzNxuN1%2BxYoXpdKpvHzvWgOrqapYMbWc6Uc45mptb4HK5uM%2FXuxqfCkI4HOGWZV%2F0%2BuuvD1248KHqZcuWxRYtepRomsYFQTjtPb8pgxthGAaysrNOC4GcczDbRkpKCjo6OvCLXyzB0089hWHDhuH222dqlmVRzlG%2Bdeu2J7Zt27Ym6Q90x6FQKMQAwOdT37VtFisvLxNO2FTkrMra2toCn88Hl8tN%2BqPogiCQmKZxPW4MWrx48b%2B89NLLX7333nvt8%2Bf%2FgLS0tJxVHOvrj4JSyjPSM076vW3bEEURnpQUVJSX44f3z0d5WRnmzPku5s%2B%2F39q5cweNx%2BO2oiiPAqCjRo2Sz1QMsUAgIJSWho6qqrp6z5495ODBg7aiqKfRkhCCeNxAW1sb0tPTuao60VcG9ATBNE0ejWnuefPmXfnn4Ac1n322seGuefPIgQMHzghCXV0dnE4nfH5%2FN%2BC2bcPj8SAWi%2BH5557Dk08%2BAbfbjUWLFuHWW29FVVWlXlNTo6iq%2BsYXX3yxKxAIkN27dxu9lcNEVZ2vx2JRVFSUU0mSThKnZBKkaTF0dHQgIyMTkkjJQPJ8SinhnPNwJCpff91149atK2%2BuO1r3j7lz7yRVVZtOA6Gurg5erxcpKSlI5iderxebq6rwowcewF%2F%2FugrTp8%2FAwoUP4eKLL0F9fb1RVlbukCTpuCTJTydcLn7WcjjZTsrKygqJorR78%2BbNtLW1lZ0KQrLmjoTDyM7O5jglCepnjU8opegKR8jll19WEAptjCuquvvuu%2B8mH330EZJCzBhDQ0MDBg0aBFmWkWTdK%2F%2F%2BMn7yk0fAGMOiRYswbdp0MMbAGOOh0Aazq6tLkmXpmc8%2F%2F7wl4XKxXg2RRAPEdrnUPzQ3N2PLli1MVb%2B5BkkGdHZ2Qo%2FH0VsO0J%2BVDJODBg0auf7TMjW%2FoGDnggU%2F5itWrIAonhigaGxsRF5eHs%2FIyCC7du7Agwt%2BjPfffw9TptyIRx99FJdccinC4S44HA40NDTomzdvViVJ2tPVFXltyZIlNDFL1LsjlPwjSVL%2Bg3MeqagoFw3D4D3zdkopjh9vh23bPDMz64IZHskwKUrisJV%2FXpkZCNxRvXjxz%2BLPP%2F8cysvL0dbWBoeikBXLl%2BPBBxegs7MTDz%2F8E8yaNStxLbXknAFbu7aUW5ZFZVl8bPfu3cbu3bvP2CU6U6DnCTu8cfz4wlX79%2B%2Bft3%2F%2FfnvEiJGipsW6k6DW1lYIgoD09HTCe9hVFwKESCTKFUXJ%2Fs1vfqNkZmbufvPNN0a%2F%2F%2F77otvtJhtDIRKLxVBcXIzp02fA4%2FEgGo12v7%2Bqqvj666%2B1vXv3uiRJXlNZuflvvXWLey3HnE73ck3TUF5eTkVROEkHWlpaoSgKT01Lg22zCzZvl3gPEtM0DsD%2F4MKHxmRnZxNFUUgyURo8eAhmz56TbLJ2l%2BeUUsTjcau0dI0IwFRVdVGys9WvzlACLXL99ddXyrK0c9u2rbSlpcWWZblbkFpamuHxeHhKSsqJkHSeDGCMwbZtCIKAFI8bHreb7Ny5k%2F%2F0ySfE5uYmMZn4GIYBRXF0d4OSV5MxBlV1oqqqSj927JhDlh2vbdy4cU8gEBhYa6ykpERYunQpU1XXGy0tLaiqquKKonQ3IFpbW%2BH3p8LlcpH%2B5gCnpq4nHl5FiscNXdfwlw8%2FxNw75%2BCOwCzy979%2FhW99azIURUE8HofP58PUqd%2FuPvUkayRJwvHj7fGKinJFkqQWSukzyebOgIakkmLodDr%2FDKBjw4YKUdd1njRC2tvbkZGRwR0OhfTX4eGcdydOJ07bhYMHD%2BC555%2FHLbd8Bz%2F76ZPgnGPxz5%2FCSy%2B9jCeeeBIPPfQwNE3DuHHjMGLECOi6fhIAsizzsrIyKxqNipIkP11VVdX%2BTXNnYL3BpBi2TZgw7oODBw%2F%2BYM%2BePfaVV44VGxuPobOjA%2BPGjYdA0T3x0Reac86hKApkSUQ0FsO6deuwcuWfUVlZCafTiRtuuAGTJ0%2FBxRdfDACIxU4kXMOHD0daWhoikUiyhd69eUVRUFtbq2%2FbttUpSdKXeXl5b%2BTl5fVpTKZP8wGK4lzR2tpyX3l5GS0qKkJXVxei0Wh3EtSXya8TIzEuEAC1R%2Bqw%2BpOP8eGHH6K29jAuv%2FxyLFjwICZOnAi%2FPxWGEUckEjnJCBFFESkpKWhvbz%2BTF8jWrl0D27aJqjp%2F0qMJcn4jMgkE6caNG7cWFBRs2bZtW1FLS4sdi8UEPR5HZlYWOddpyw4HFFmCYVqorKxEMLgSFeXl4JyjuLgYCxcuxIgRIyCKIjRNQ2dnR7fx2hNEWZbh9%2FvR1NTUzQDGGJxOJ778cqe2f%2F8BlyzLqzZt2lR%2BQYekEiNxzO1Wl7e1tRVt2rQJKSkpYMzmmRmZZzxtSimcThcECjQ1tyC4thQffBDE7t27kZeXh7lz5%2BHaa69FZmYmLMuCpmndjY%2BzDVYJgoDU1FTU1NTAMAyIoghBEKDrullaulaklOqSJD92rrDXbwCSYqgorg8J6Xj%2Bs882po8ZM4bLsoOnpqUmjJAeRoTLCcY5vvxyFz78y1%2BwZs3fEIlEMGHCBDz77C8xZswVUFUFmqahq6urm%2Ba9RZEkOIMGDUIsFoOua3C7PVAUBWVl6%2BMtLc1uVVWfr6ysrOnrcFR%2FNICXlJSI69ev7xw7tuB%2FHzp0aGFjY5OdlpZGL7ooj1BK4HA44JAldHR2YW3pGqxcuRLV1duQlpaGm2%2F%2BDiZNmoS8vDwwxhI07zyN5udydjnnSEsbBNM0EQ5HMGhQOlpaWuKhUEgRRamBMfwq0djtl9XcJxFMmCWEEOENXdfvj0QiMqUUL764jD766CJEwhF88EEQH3%2F8EZqbm5Gfn48nnngChYXj4PF4EI%2FHuye9k3PBA0mUUlNTwRhDV1cnHA4HX79%2BvRWLxRyq6vxZVVVVV%2BLuswsOQEJNuaZp%2B5xOtUmW5cG2bbOP%2FrqK7NyxA7FYDAAwadINmDx5MoYOHQrGOGKxKDo6Orrt9aRGnNruPlcSRcgJAHw%2BHygliMfjqK2t1bZvr3bKsrS1qqrqT%2F2dEh%2FIoCRkWfYCSLNPuBTE5%2FPbjY2NJCsry5ox41b7kksuBmOMHDlyhCiKQlRVpS6Xi3wzIEEIwJP1%2BmlfyVrjTJa8ZVnwer1wOl1oamq2v%2F56N2WMEUWRHzmf1nN%2F8lcKAOPGFYYkSSq2LOukVplhGGCMQZIkKIoCRVHgcrngcrnh9abYXq%2BP%2BXw%2B5vf7uNfrQ0pKCtxuN1FVlSiKQiVJIt%2BM0JwMVNLz6%2BzsxGOPLYJpmgbnXDZN653q6up7Bnr6%2FWYAAGaa1l2UklcJIVfYtv0VIey3nAtEUdQczu0sxniWpmlZkUg0s7m5eRBjLJVz7gWg9PxsgCiK3WOzTqcTbrcbHo%2BHe70%2B2%2B%2F3M5%2FPx3w%2BHzweDzweD1FVVXjrrT%2BIHR0dkCRJTJgk7wIgyc8j%2FrMBYACwa9euwwCmlZSUKKFQSD%2BXes%2BaNcsdDod9hmGk2raRYZos07KsbMZYdjyuZ8VisazW1pZ022apjDEfANepQDkcDhBCEA6HuSzLhHPOABDG2B0Ays7LfxjIpEoiMugAaHIQsecpJD%2FaxjlnwWAwAiAC4GhvQD3yyCPqvn37vLqupzJmpJsmz7QsK4tzOyce1zM5x5Wqqubbtm0CsE88O08777mj8%2Fxf3sfXJ4kxnNPomgix5wxdkydP9nZ2Hv9KFKU8zhk4ByzLvrG6uvrT89GACza6diGGwHoOavUEKhKJkOrqavPKK68cIorig4Rwn2Wx%2F9y%2BfXs5Bvh5wf8fF%2FlnHOD%2FAaRsQhCQ8p9bAAAAAElFTkSuQmCC&logoWidth=18&labelColor=white)](https://huggingface.co/PaddlePaddle/PP-OCRv6_medium_rec_onnx)

**🔥 [Official Website](https://www.paddleocr.com)**
**📝 [Technical Report](https://arxiv.org/pdf/2606.13108)**
</div>

<div align="center">
<img src="https://cdn-uploads.huggingface.co/production/uploads/684ba591e717a30275a1b76a/0XIrg0UmmOvplnPjmsmK3.png" width="800"/>
</div>

## PP-OCRv6 Overview

PP-OCRv6 is a lightweight OCR system that combines architectural innovation with data-centric optimization. It redesigns the backbone, detection neck, and recognition neck around a unified MetaFormer-style building block with structural reparameterization. Three model tiers (medium, small, tiny) share the same block primitives, covering deployment scenarios from server to edge.

### Key Features

1. **Unified and Scalable Model Family:** A three-tier OCR model family spanning 1.5M to 34.5M parameters. PP-OCRv6_medium achieves 86.2% detection Hmean and 83.2% recognition accuracy, outperforming PP-OCRv5_server by +4.6% and +5.1% respectively.

2. **Lightweight Architectural Innovations:** (i) LCNetV4, a MetaFormer-style lightweight backbone with structural reparameterization; (ii) RepLKFPN, a detection neck with dilated reparameterizable depthwise convolutions; (iii) EncoderWithLightSVTR, a recognition neck with local-global attention and additive skip connections.

3. **Multi-Language and Scenario Support:** Supports 50 languages and diverse industrial scenes (digital displays, dot-matrix characters, tire prints, etc.), surpassing Qwen3-VL-235B, GPT-5.5, and Gemini-3.1-Pro with orders of magnitude fewer parameters.

# PP-OCRv6_medium_rec

## Introduction

<div align="center">
<img src="https://cdn-uploads.huggingface.co/production/uploads/684ba591e717a30275a1b76a/9hBYIA-pO6jhnG6bcZEaf.png" width="600"/>

PP-OCRv6 text recognition architecture overview
</div>

PP-OCRv6_medium_rec is the largest recognition model in the PP-OCRv6 series. It uses LCNetV4 as the backbone and EncoderWithLightSVTR as the recognition neck, with a CTC+NRTR multi-head decoder. The model supports 50 languages and contains 19M parameters. The key accuracy metrics are as follows:

| Model | W-Avg | Handwritten CN | Handwritten EN | Printed CN | Printed EN | TC | Ancient | JP | Confusable | Special | General | Pinyin | Artistic | Industrial | Screen | Card |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| GPT-5.5 | 64.2 | 19.2 | 56.9 | 75.7 | 82.2 | 57.5 | 63.7 | 58.6 | 49.1 | 48.3 | 67.7 | 50.4 | 53.0 | 62.4 | 67.7 | 71.1 |
| Qwen3-VL-235B | 74.9 | 49.7 | 73.2 | 82.3 | 86.2 | 76.4 | 33.6 | 66.2 | 56.1 | 49.0 | 82.5 | 76.5 | 69.6 | 74.7 | 73.8 | 78.7 |
| Kimi-K2.6 | 62.9 | 31.0 | 58.4 | 76.8 | 80.9 | 62.7 | 16.5 | 54.1 | 43.5 | 38.0 | 68.0 | 45.2 | 59.9 | 57.1 | 58.4 | 68.4 |
| MiniMax-M3 | 54.1 | 15.5 | 60.3 | 63.5 | 81.5 | 53.2 | 2.2 | 43.7 | 42.2 | 42.8 | 53.8 | 50.3 | 44.3 | 44.1 | 56.6 | 67.0 |
| Gemini-3.1-Pro | 71.4 | 46.4 | 73.0 | 80.0 | 90.5 | 69.5 | 18.0 | 67.2 | 54.4 | 50.3 | 74.6 | 75.9 | 63.1 | 69.1 | 73.2 | 75.9 |
| PP-OCRv5_server | 78.1 | 58.0 | 59.6 | 90.1 | 85.1 | 74.7 | 60.4 | 73.7 | 59.4 | 56.8 | 86.5 | 74.4 | 64.0 | 70.2 | 68.1 | 87.6 |
| PP-OCRv5_mobile | 73.7 | 41.7 | 50.9 | 86.0 | 86.0 | 72.0 | 57.8 | 75.8 | 55.7 | 54.8 | 80.7 | 72.5 | 54.0 | 59.3 | 57.6 | 81.7 |
| **PP-OCRv6_medium** | **83.2** | **62.1** | **67.8** | **91.5** | **94.1** | **78.6** | **72.4** | **90.5** | **64.9** | **61.7** | **87.5** | **78.1** | **71.2** | **77.4** | **82.5** | **88.1** |
| PP-OCRv6_small | 81.3 | 57.6 | 61.1 | 90.5 | 93.3 | 77.0 | 71.1 | 88.2 | 64.1 | 60.2 | 85.7 | 75.9 | 68.4 | 76.4 | 79.7 | 86.9 |
| PP-OCRv6_tiny | 73.5 | 40.1 | 39.3 | 86.7 | 88.4 | 65.0 | 68.4 | 89.8 | 52.3 | 57.1 | 78.0 | 65.4 | 54.7 | 62.1 | 71.2 | 80.5 |

## Quick Start

### Installation

1. PaddleOCR

```bash
# Install the basic version
pip install paddleocr

# Install the full version (includes all features)
pip install "paddleocr[all]"
```

### Model Usage

You can quickly experience the functionality with a single command:

```bash
paddleocr text_recognition \
    --model_name PP-OCRv6_medium_rec \
    -i https://cdn-uploads.huggingface.co/production/uploads/681c1ecd9539bdde5ae1733c/2PZfbirjfxA88695lRmgk.jpeg
```

You can also integrate the model inference of the text recognition module into your project. Before running the following code, please download the sample image to your local machine.

```python
from paddleocr import TextRecognition
model = TextRecognition(model_name="PP-OCRv6_medium_rec")
output = model.predict(input="2PZfbirjfxA88695lRmgk.jpeg", batch_size=1)
for res in output:
    res.print()
    res.save_to_json(save_path="./output/res.json")
```

After running, the obtained result is as follows:

```json
{'res': {'input_path': '2PZfbirjfxA88695lRmgk.jpeg', 'page_index': None, 'rec_text': 'day as a reminder of the', 'rec_score': 0.9857}}
```

The visualized image is as follows:

![image/jpeg](https://cdn-uploads.huggingface.co/production/uploads/681c1ecd9539bdde5ae1733c/ZDuFDBgLkRcinubSjIvpN.png)

<!-- TODO: Update document links to PP-OCRv6 official documentation when available -->
For details about usage command and descriptions of parameters, please refer to the [Document](https://paddlepaddle.github.io/PaddleOCR/latest/en/version3.x/module_usage/text_recognition.html).

### Pipeline Usage

The general OCR pipeline is used to solve text recognition tasks by extracting text information from images. The pipeline consists of several modules:
* Document Image Orientation Classification Module (Optional)
* Text Image Unwarping Module (Optional)
* Text Line Orientation Classification Module (Optional)
* Text Detection Module
* Text Recognition Module

Run a single command to quickly experience the OCR pipeline:

```bash
paddleocr ocr -i https://cdn-uploads.huggingface.co/production/uploads/681c1ecd9539bdde5ae1733c/3ul2Rq4Sk5Cn-l69D695U.png \
    --text_detection_model_name PP-OCRv6_medium_det \
    --text_recognition_model_name PP-OCRv6_medium_rec \
    --use_doc_orientation_classify False \
    --use_doc_unwarping False \
    --use_textline_orientation True \
    --save_path ./output \
    --device gpu:0
```

If save_path is specified, the visualization results will be saved under `save_path`. The visualization output is shown below:

![image/jpeg](https://cdn-uploads.huggingface.co/production/uploads/681c1ecd9539bdde5ae1733c/uhwveSX8KU_wMkyU4jEaw.png)

For project integration:

```python
from paddleocr import PaddleOCR

ocr = PaddleOCR(
    text_detection_model_name="PP-OCRv6_medium_det",
    text_recognition_model_name="PP-OCRv6_medium_rec",
    use_doc_orientation_classify=False,
    use_doc_unwarping=False,
    use_textline_orientation=True,
)
result = ocr.predict("./3ul2Rq4Sk5Cn-l69D695U.png")
for res in result:
    res.print()
    res.save_to_img("output")
    res.save_to_json("output")
```

<!-- TODO: Update document links to PP-OCRv6 official documentation when available -->
For details about usage command and descriptions of parameters, please refer to the [Document](https://paddlepaddle.github.io/PaddleOCR/latest/en/version3.x/pipeline_usage/OCR.html#2-quick-start).

## Links

[PaddleOCR Repo](https://github.com/paddlepaddle/paddleocr)

[PaddleOCR Documentation](https://paddlepaddle.github.io/PaddleOCR/latest/en/index.html)

## Citation

```bibtex
@misc{zhang2026ppocrv6,
  title={PP-OCRv6: From 1.5M to 34.5M Parameters, Surpassing Billion-Scale VLMs on OCR Tasks},
  author={Yubo Zhang and Xueqing Wang and Manhui Lin and Yue Zhang and Penglongyi Deng and Ting Sun and Tingquan Gao and Zelun Zhang and Jiaxuan Liu and Changda Zhou and Hongen Liu and Suyin Liang and Cheng Cui and Yi Liu and Dianhai Yu and Yanjun Ma},
  year={2026},
  eprint={2606.13108},
  archivePrefix={arXiv},
  primaryClass={cs.CV},
  url={https://arxiv.org/abs/2606.13108},
}
```
