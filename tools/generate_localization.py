from __future__ import annotations

import json
import re
import sqlite3
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parent.parent
DB_PATH = ROOT / "data" / "starrupture.sqlite3"
OUT_DIR = ROOT / "data" / "localization"

LANGUAGE_NAMES = {
    "en": "English",
    "ru": "Russian",
    "de": "German",
    "uk": "Ukrainian",
}

DESCRIPTION_TRANSLATIONS = {
    "Crafting Material. Used in various recipes. Can be exported.": {
        "ru": "Производственный материал. Используется в различных рецептах. Можно экспортировать.",
        "de": "Fertigungsmaterial. Wird in verschiedenen Rezepten verwendet. Kann exportiert werden.",
        "uk": "Виробничий матеріал. Використовується в різних рецептах. Можна експортувати.",
    },
    "Crafting Material. Used in various recipes.": {
        "ru": "Производственный материал. Используется в различных рецептах.",
        "de": "Fertigungsmaterial. Wird in verschiedenen Rezepten verwendet.",
        "uk": "Виробничий матеріал. Використовується в різних рецептах.",
    },
    "Used in Recipe Station to unlock item production.": {
        "ru": "Используется на станции рецептов, чтобы разблокировать производство предмета.",
        "de": "Wird in der Rezeptstation verwendet, um die Produktion des Gegenstands freizuschalten.",
        "uk": "Використовується на станції рецептів, щоб розблокувати виробництво предмета.",
    },
    "Obtained from analyzing valuable items": {
        "ru": "Получается при анализе ценных предметов.",
        "de": "Wird durch Analyse wertvoller Gegenstände erhalten.",
        "uk": "Отримується під час аналізу цінних предметів.",
    },
    "Biomass": {"ru": "Биомасса", "de": "Biomasse", "uk": "Біомаса"},
    "Coal": {"ru": "Уголь", "de": "Kohle", "uk": "Вугілля"},
    "Copper": {"ru": "Медь", "de": "Kupfer", "uk": "Мідь"},
}

PHRASES = {
    "ru": {
        "Anti-Radiation": "Противорадиационные",
        "Basic Building Material": "Базовый строительный материал",
        "Elementary Building Material": "Элементарный строительный материал",
        "Intermediate Building Material": "Промежуточный строительный материал",
        "Quartz Building Material": "Кварцевый строительный материал",
        "Heat-Resistant Sheet": "Жаропрочный лист",
        "Heat Shield": "Тепловой щит",
        "Control Systems": "Системы управления",
        "Data Point": "Точка данных",
        "Synthetic DNA": "Синтетическая ДНК",
        "Synthetic Protein": "Синтетический белок",
        "Synthetic Resin": "Синтетическая смола",
        "Synthetic Fertilizer": "Синтетическое удобрение",
        "Universal Space Soil": "Универсальный космический грунт",
        "Self-Sustained Habitat": "Автономный модуль обитания",
        "Mars Habitat Base": "База марсианского модуля",
        "Molecular Scalpel": "Молекулярный скальпель",
        "Carbon Sonar": "Углеродный сонар",
        "Genome Alternator": "Геномный альтернатор",
        "Containment Vessel": "Контейнер-резервуар",
        "Tracking Device": "Устройство слежения",
        "Payload Stage": "Грузовая ступень",
        "Propulsion Stage": "Двигательная ступень",
        "Onboard Instruments": "Бортовые приборы",
        "Micro Satellite": "Микроспутник",
        "Rocket Engine": "Ракетный двигатель",
        "Rocket Fuel": "Ракетное топливо",
        "Missile Housing": "Корпус ракеты",
        "Rare Ore": "Редкая руда",
        "Titanium Ore": "Титановая руда",
        "Titanium Bar": "Титановый слиток",
        "Titanium Rod": "Титановый стержень",
        "Titanium Sheet": "Титановый лист",
        "Titanium Beam": "Титановая балка",
        "Titanium Housing": "Титановый корпус",
        "Wolfram Ore": "Вольфрамовая руда",
        "Wolfram Bar": "Вольфрамовый слиток",
        "Wolfram Plate": "Вольфрамовая пластина",
        "Wolfram Powder": "Вольфрамовый порошок",
        "Wolfram Wire": "Вольфрамовая проволока",
        "Calcium Ore": "Кальциевая руда",
        "Calcium Powder": "Кальциевый порошок",
        "Calcium Block": "Кальциевый блок",
        "CoalOre": "Угольная руда",
        "CopperOre": "Медная руда",
        "CopperBar": "Медный слиток",
    },
    "de": {
        "Anti-Radiation": "Strahlenschutz",
        "Basic Building Material": "Einfaches Baumaterial",
        "Elementary Building Material": "Elementares Baumaterial",
        "Intermediate Building Material": "Mittleres Baumaterial",
        "Quartz Building Material": "Quarz-Baumaterial",
        "Heat-Resistant Sheet": "Hitzebeständige Platte",
        "Heat Shield": "Hitzeschild",
        "Control Systems": "Steuerungssysteme",
        "Data Point": "Datenpunkt",
        "Synthetic DNA": "Synthetische DNA",
        "Synthetic Protein": "Synthetisches Protein",
        "Synthetic Resin": "Synthetisches Harz",
        "Synthetic Fertilizer": "Synthetischer Dünger",
        "Universal Space Soil": "Universeller Weltraumboden",
        "Self-Sustained Habitat": "Autarkes Habitat",
        "Mars Habitat Base": "Mars-Habitat-Basis",
        "Molecular Scalpel": "Molekulares Skalpell",
        "Carbon Sonar": "Kohlenstoff-Sonar",
        "Genome Alternator": "Genom-Alternator",
        "Containment Vessel": "Eindämmungsbehälter",
        "Tracking Device": "Ortungsgerät",
        "Payload Stage": "Nutzlaststufe",
        "Propulsion Stage": "Antriebsstufe",
        "Onboard Instruments": "Bordinstrumente",
        "Micro Satellite": "Mikrosatellit",
        "Rocket Engine": "Raketentriebwerk",
        "Rocket Fuel": "Raketentreibstoff",
        "Missile Housing": "Raketengehäuse",
        "Rare Ore": "Seltenes Erz",
        "Titanium Ore": "Titanerz",
        "Titanium Bar": "Titanbarren",
        "Titanium Rod": "Titanstab",
        "Titanium Sheet": "Titanblech",
        "Titanium Beam": "Titanträger",
        "Titanium Housing": "Titangehäuse",
        "Wolfram Ore": "Wolframerz",
        "Wolfram Bar": "Wolframbarren",
        "Wolfram Plate": "Wolframplatte",
        "Wolfram Powder": "Wolframpulver",
        "Wolfram Wire": "Wolframdraht",
        "Calcium Ore": "Kalziumerz",
        "Calcium Powder": "Kalziumpulver",
        "Calcium Block": "Kalziumblock",
        "CoalOre": "Kohleerz",
        "CopperOre": "Kupfererz",
        "CopperBar": "Kupferbarren",
    },
    "uk": {
        "Anti-Radiation": "Протирадіаційні",
        "Basic Building Material": "Базовий будівельний матеріал",
        "Elementary Building Material": "Елементарний будівельний матеріал",
        "Intermediate Building Material": "Проміжний будівельний матеріал",
        "Quartz Building Material": "Кварцовий будівельний матеріал",
        "Heat-Resistant Sheet": "Жаростійкий лист",
        "Heat Shield": "Тепловий щит",
        "Control Systems": "Системи керування",
        "Data Point": "Точка даних",
        "Synthetic DNA": "Синтетична ДНК",
        "Synthetic Protein": "Синтетичний білок",
        "Synthetic Resin": "Синтетична смола",
        "Synthetic Fertilizer": "Синтетичне добриво",
        "Universal Space Soil": "Універсальний космічний грунт",
        "Self-Sustained Habitat": "Автономний житловий модуль",
        "Mars Habitat Base": "База марсіанського модуля",
        "Molecular Scalpel": "Молекулярний скальпель",
        "Carbon Sonar": "Вуглецевий сонар",
        "Genome Alternator": "Геномний альтернатор",
        "Containment Vessel": "Контейнмент-резервуар",
        "Tracking Device": "Пристрій стеження",
        "Payload Stage": "Вантажний ступінь",
        "Propulsion Stage": "Рушійний ступінь",
        "Onboard Instruments": "Бортові прилади",
        "Micro Satellite": "Мікросупутник",
        "Rocket Engine": "Ракетний двигун",
        "Rocket Fuel": "Ракетне паливо",
        "Missile Housing": "Корпус ракети",
        "Rare Ore": "Рідкісна руда",
        "Titanium Ore": "Титанова руда",
        "Titanium Bar": "Титановий зливок",
        "Titanium Rod": "Титановий стрижень",
        "Titanium Sheet": "Титановий лист",
        "Titanium Beam": "Титанова балка",
        "Titanium Housing": "Титановий корпус",
        "Wolfram Ore": "Вольфрамова руда",
        "Wolfram Bar": "Вольфрамовий зливок",
        "Wolfram Plate": "Вольфрамова пластина",
        "Wolfram Powder": "Вольфрамовий порошок",
        "Wolfram Wire": "Вольфрамовий дріт",
        "Calcium Ore": "Кальцієва руда",
        "Calcium Powder": "Кальцієвий порошок",
        "Calcium Block": "Кальцієвий блок",
        "CoalOre": "Вугільна руда",
        "CopperOre": "Мідна руда",
        "CopperBar": "Мідний зливок",
    },
}

WORDS = {
    "ru": {
        "Accumulator": "Аккумулятор", "Aerogel": "Аэрогель", "Airlock": "Шлюз",
        "Antimatter": "Антиматерии", "Applicator": "Аппликатор", "Arc": "Дуговой",
        "Bar": "Слиток", "Battery": "Батарея", "Biofilament": "Биофиламент", "Biomass": "Биомасса",
        "Bioprinter": "Биопринтер", "Blueprint": "Чертеж", "Block": "Блок", "Bomb": "Бомба",
        "Building": "Строительный", "Calcium": "Кальций", "Calcite": "Кальцитовые", "Cell": "Ячейка",
        "Ceramics": "Керамика", "Chemicals": "Химикаты", "Coil": "Катушка", "Condenser": "Конденсатор",
        "Converter": "Преобразователь", "Copper": "Медный", "Covers": "покрытия", "Cyclotrone": "Циклотрон",
        "Dome": "Купол", "Electromagnet": "Электромагнит", "Electromagnetic": "Электромагнитная",
        "Electronics": "Электроника", "Epoxy": "Эпоксид", "Explosive": "Взрывчатка",
        "Fuel": "Топливо", "Generator": "Генератор", "Glass": "Стекло", "Goethite": "Гетит",
        "Gold": "Золото", "Grenade": "Граната", "Hardening": "Отвердитель", "Helium": "Гелий",
        "Housing": "Корпус", "Impeller": "Крыльчатка", "Ingot": "Слиток", "Injector": "Инжектор",
        "Insulator": "Изолятор", "Ion": "Ионный", "Lattice": "Решетка", "Lens": "Линза",
        "Liquid": "Жидкий", "Material": "материал", "Meteor": "Метеоритное", "Missile": "Ракета",
        "Nanofibre": "Нановолокно", "Nanosyringe": "Наношприц", "Neutrino": "Нейтринная",
        "Nozzle": "Сопло", "Ore": "Руда", "Organic": "Органическое", "Panaceum": "Панацеум",
        "Plate": "Пластина", "Powder": "Порошок", "Pressure": "Давление", "Pressurized": "Сжатый",
        "Pump": "Насос", "Pyrite": "Пирит", "Quantum": "Квантовый", "Refined": "Очищенный",
        "Reinforced": "Усиленный", "Resonator": "Резонатор", "Rod": "Стержень", "Rotor": "Ротор",
        "Scanner": "Сканер", "Scafolding": "Леса", "Sand": "Песок", "Satellite": "Спутник",
        "Sheet": "Лист", "Silica": "Кремнеземный", "Soil": "Грунт", "Stator": "Статор",
        "Stone": "Камень", "Substance": "Вещество", "Sulphur": "Сера", "Sulphuric": "Серная",
        "Superconductor": "Сверхпроводник", "Supermagnet": "Супермагнит", "Syringe": "Шприц",
        "Tank": "Бак", "Titanium": "Титан", "Tube": "Трубка", "Turbine": "Турбина",
        "Uberfilament": "Уберфиламент", "Valve": "Клапан", "Wolfram": "Вольфрам", "Wire": "Проволока",
    },
    "de": {
        "Accumulator": "Akkumulator", "Aerogel": "Aerogel", "Airlock": "Luftschleuse",
        "Antimatter": "Antimaterie", "Applicator": "Applikator", "Arc": "Lichtbogen",
        "Bar": "Barren", "Battery": "Batterie", "Biofilament": "Biofilament", "Biomass": "Biomasse",
        "Bioprinter": "Biodrucker", "Blueprint": "Bauplan", "Block": "Block", "Bomb": "Bombe",
        "Building": "Bau", "Calcium": "Kalzium", "Calcite": "Kalzit", "Cell": "Zelle",
        "Ceramics": "Keramik", "Chemicals": "Chemikalien", "Coil": "Spule", "Condenser": "Kondensator",
        "Converter": "Konverter", "Copper": "Kupfer", "Covers": "Abdeckungen", "Cyclotrone": "Zyklotron",
        "Dome": "Kuppel", "Electromagnet": "Elektromagnet", "Electromagnetic": "Elektromagnetische",
        "Electronics": "Elektronik", "Epoxy": "Epoxid", "Explosive": "Sprengstoff",
        "Fuel": "Treibstoff", "Generator": "Generator", "Glass": "Glas", "Goethite": "Goethit",
        "Gold": "Gold", "Grenade": "Granate", "Hardening": "Härter", "Helium": "Helium",
        "Housing": "Gehäuse", "Impeller": "Laufrad", "Ingot": "Barren", "Injector": "Injektor",
        "Insulator": "Isolator", "Ion": "Ionen", "Lattice": "Gitter", "Lens": "Linse",
        "Liquid": "Flüssiges", "Material": "Material", "Meteor": "Meteor", "Missile": "Rakete",
        "Nanofibre": "Nanofaser", "Nanosyringe": "Nanospritze", "Neutrino": "Neutrino",
        "Nozzle": "Düse", "Ore": "Erz", "Organic": "Organische", "Panaceum": "Panaceum",
        "Plate": "Platte", "Powder": "Pulver", "Pressure": "Druck", "Pressurized": "Komprimiertes",
        "Pump": "Pumpe", "Pyrite": "Pyrit", "Quantum": "Quanten", "Refined": "Raffiniertes",
        "Reinforced": "Verstärkter", "Resonator": "Resonator", "Rod": "Stab", "Rotor": "Rotor",
        "Scanner": "Scanner", "Scafolding": "Gerüst", "Sand": "Sand", "Satellite": "Satellit",
        "Sheet": "Blech", "Silica": "Siliziumdioxid", "Soil": "Boden", "Stator": "Stator",
        "Stone": "Stein", "Substance": "Substanz", "Sulphur": "Schwefel", "Sulphuric": "Schwefel",
        "Superconductor": "Supraleiter", "Supermagnet": "Supermagnet", "Syringe": "Spritze",
        "Tank": "Tank", "Titanium": "Titan", "Tube": "Rohr", "Turbine": "Turbine",
        "Uberfilament": "Uberfilament", "Valve": "Ventil", "Wolfram": "Wolfram", "Wire": "Draht",
    },
    "uk": {
        "Accumulator": "Акумулятор", "Aerogel": "Аерогель", "Airlock": "Шлюз",
        "Antimatter": "Антиматерії", "Applicator": "Аплікатор", "Arc": "Дуговий",
        "Bar": "Зливок", "Battery": "Батарея", "Biofilament": "Біофіламент", "Biomass": "Біомаса",
        "Bioprinter": "Біопринтер", "Blueprint": "Креслення", "Block": "Блок", "Bomb": "Бомба",
        "Building": "Будівельний", "Calcium": "Кальцій", "Calcite": "Кальцитові", "Cell": "Комірка",
        "Ceramics": "Кераміка", "Chemicals": "Хімікати", "Coil": "Котушка", "Condenser": "Конденсатор",
        "Converter": "Перетворювач", "Copper": "Мідний", "Covers": "покриття", "Cyclotrone": "Циклотрон",
        "Dome": "Купол", "Electromagnet": "Електромагніт", "Electromagnetic": "Електромагнітна",
        "Electronics": "Електроніка", "Epoxy": "Епоксид", "Explosive": "Вибухівка",
        "Fuel": "Паливо", "Generator": "Генератор", "Glass": "Скло", "Goethite": "Гетит",
        "Gold": "Золото", "Grenade": "Граната", "Hardening": "Затверджувач", "Helium": "Гелій",
        "Housing": "Корпус", "Impeller": "Крильчатка", "Ingot": "Зливок", "Injector": "Інжектор",
        "Insulator": "Ізолятор", "Ion": "Іонний", "Lattice": "Гратка", "Lens": "Лінза",
        "Liquid": "Рідкий", "Material": "матеріал", "Meteor": "Метеоритне", "Missile": "Ракета",
        "Nanofibre": "Нановолокно", "Nanosyringe": "Наношприц", "Neutrino": "Нейтринна",
        "Nozzle": "Сопло", "Ore": "Руда", "Organic": "Органічна", "Panaceum": "Панацеум",
        "Plate": "Пластина", "Powder": "Порошок", "Pressure": "Тиск", "Pressurized": "Стиснений",
        "Pump": "Насос", "Pyrite": "Пірит", "Quantum": "Квантовий", "Refined": "Очищений",
        "Reinforced": "Посилений", "Resonator": "Резонатор", "Rod": "Стрижень", "Rotor": "Ротор",
        "Scanner": "Сканер", "Scafolding": "Риштування", "Sand": "Пісок", "Satellite": "Супутник",
        "Sheet": "Лист", "Silica": "Кремнеземний", "Soil": "Грунт", "Stator": "Статор",
        "Stone": "Камінь", "Substance": "Речовина", "Sulphur": "Сірка", "Sulphuric": "Сірчана",
        "Superconductor": "Надпровідник", "Supermagnet": "Супермагніт", "Syringe": "Шприц",
        "Tank": "Бак", "Titanium": "Титан", "Tube": "Трубка", "Turbine": "Турбіна",
        "Uberfilament": "Уберфіламент", "Valve": "Клапан", "Wolfram": "Вольфрам", "Wire": "Дріт",
    },
}

BUILDINGS = {
    "ru": {
        "acid-extractor": "Экстрактор серы", "assembler": "Сборщик", "crafter": "Фабрикатор",
        "crafter-tier2": "Фабрикатор v.2", "factory": "Конструкторизатор", "factory-tier2": "Конструкторизатор v.2",
        "forge": "Пирокузня", "furnace": "Печь", "furnace-tier2": "Печь v.2", "gas-extractor": "Экстрактор гелия-3",
        "hammer": "Мега-пресс", "laser-drill": "Лазерный бур", "liquid-extractor": "Нефтяной экстрактор",
        "mechanical-drill": "Рудный экскаватор", "mechanical-drill-tier2": "Рудный экскаватор v.2",
        "military-assembler": "Фактурер", "pressurizer": "Очиститель", "refinery": "Прессуризатор",
        "smelter": "Плавильня", "synthetizer": "Компоновщик", "synthetizer-tier2": "Компоновщик v.2",
    },
    "de": {
        "acid-extractor": "Schwefelextraktor", "assembler": "Montierer", "crafter": "Fabrikator",
        "crafter-tier2": "Fabrikator v.2", "factory": "Konstruktorisierer", "factory-tier2": "Konstruktorisierer v.2",
        "forge": "Pyro-Schmiede", "furnace": "Ofen", "furnace-tier2": "Ofen v.2", "gas-extractor": "Helium-3-Extraktor",
        "hammer": "Mega-Presse", "laser-drill": "Laserbohrer", "liquid-extractor": "Ölextraktor",
        "mechanical-drill": "Erzbagger", "mechanical-drill-tier2": "Erzbagger v.2",
        "military-assembler": "Fakturierer", "pressurizer": "Raffinerie", "refinery": "Druckbeaufschlager",
        "smelter": "Schmelzer", "synthetizer": "Compounder", "synthetizer-tier2": "Compounder v.2",
    },
    "uk": {
        "acid-extractor": "Екстрактор сірки", "assembler": "Складальник", "crafter": "Фабрикатор",
        "crafter-tier2": "Фабрикатор v.2", "factory": "Конструкторизатор", "factory-tier2": "Конструкторизатор v.2",
        "forge": "Пірокузня", "furnace": "Піч", "furnace-tier2": "Піч v.2", "gas-extractor": "Екстрактор гелію-3",
        "hammer": "Мега-прес", "laser-drill": "Лазерний бур", "liquid-extractor": "Нафтовий екстрактор",
        "mechanical-drill": "Рудний екскаватор", "mechanical-drill-tier2": "Рудний екскаватор v.2",
        "military-assembler": "Фактурер", "pressurizer": "Очисник", "refinery": "Пресуризатор",
        "smelter": "Плавильня", "synthetizer": "Компаундер", "synthetizer-tier2": "Компаундер v.2",
    },
}

CORPORATIONS = {
    "ru": {
        "clever": "Clever Robotics", "future": "Future Health Solutions", "griffiths": "Griffith Blue Corporation",
        "moon": "Moon Energy Corporation", "selenian": "Селенианская корпорация", "starting": "Учебная корпорация",
    },
    "de": {
        "clever": "Clever Robotics", "future": "Future Health Solutions", "griffiths": "Griffith Blue Corporation",
        "moon": "Moon Energy Corporation", "selenian": "Selenian Corporation", "starting": "Trainingskonzern",
    },
    "uk": {
        "clever": "Clever Robotics", "future": "Future Health Solutions", "griffiths": "Griffith Blue Corporation",
        "moon": "Moon Energy Corporation", "selenian": "Селеніанська корпорація", "starting": "Навчальна корпорація",
    },
}

UI = {
    "transport.default_message": {
        "en": "Default rail tier speeds for planner recommendations.",
        "ru": "Скорости уровней рельсов по умолчанию для рекомендаций планировщика.",
        "de": "Standardgeschwindigkeiten der Schienenstufen für Planerempfehlungen.",
        "uk": "Стандартні швидкості рівнів рейок для рекомендацій планувальника.",
    }
}


def split_name(value: str) -> list[str]:
    value = re.sub(r"([a-z])([A-Z])", r"\1 \2", value)
    return re.split(r"([ -])", value)


def translate_name(value: str, language: str) -> tuple[str, bool]:
    if language == "en":
        return value, False
    phrase_map = PHRASES[language]
    if value in phrase_map:
        return phrase_map[value], True
    suffix = ""
    base = value
    if " - Blueprint" in base:
        base = base.replace(" - Blueprint", "")
        suffix = {
            "ru": " - Чертеж",
            "de": " - Bauplan",
            "uk": " - Креслення",
        }[language]
    for phrase, translated in sorted(phrase_map.items(), key=lambda item: len(item[0]), reverse=True):
        base = base.replace(phrase, translated)
    words = WORDS[language]
    translated_parts: list[str] = []
    generated = False
    for part in split_name(base):
        translated_parts.append(words.get(part, part))
        generated = generated or part in words
    result = "".join(translated_parts).replace("  ", " ").strip() + suffix
    return result if result else value, generated or bool(suffix)


def translate_description(value: str | None, language: str) -> tuple[str | None, bool]:
    if not value or language == "en":
        return value, False
    if value in DESCRIPTION_TRANSLATIONS:
        return DESCRIPTION_TRANSLATIONS[value][language], True
    translated = value
    replacements = {
        "ru": [
            ("Basic production building.", "Базовое производственное здание."),
            ("Intermediate production building.", "Промежуточное производственное здание."),
            ("Advanced production building.", "Продвинутое производственное здание."),
            ("Improved", "Улучшенное"),
            ("Used to extract", "Используется для добычи"),
            ("Can be placed only on", "Можно разместить только на"),
            ("deposits", "месторождениях"),
            ("Refines raw", "Перерабатывает сырой"),
            ("into Products", "в продукты"),
            ("Utilizes", "Использует"),
            ("Utilises", "Использует"),
            ("heat", "нагрев"),
            ("chemical reactions", "химические реакции"),
            ("high speed machining", "высокоскоростную обработку"),
            ("nanoscale printing", "нанопечать"),
            ("to make Products", "для производства продуктов"),
            ("Offers optimized recipes designed for higher production output.", "Предлагает оптимизированные рецепты для большей производительности."),
        ],
        "de": [
            ("Basic production building.", "Einfaches Produktionsgebäude."),
            ("Intermediate production building.", "Mittleres Produktionsgebäude."),
            ("Advanced production building.", "Fortgeschrittenes Produktionsgebäude."),
            ("Improved", "Verbessertes"),
            ("Used to extract", "Wird zum Fördern von"),
            ("Can be placed only on", "Kann nur auf"),
            ("deposits", "Vorkommen platziert werden"),
            ("Refines raw", "Raffiniert rohes"),
            ("into Products", "zu Produkten"),
            ("Utilizes", "Nutzt"),
            ("Utilises", "Nutzt"),
            ("heat", "Hitze"),
            ("chemical reactions", "chemische Reaktionen"),
            ("high speed machining", "Hochgeschwindigkeitsbearbeitung"),
            ("nanoscale printing", "Nanodruck"),
            ("to make Products", "zur Herstellung von Produkten"),
            ("Offers optimized recipes designed for higher production output.", "Bietet optimierte Rezepte für höhere Produktionsleistung."),
        ],
        "uk": [
            ("Basic production building.", "Базова виробнича будівля."),
            ("Intermediate production building.", "Проміжна виробнича будівля."),
            ("Advanced production building.", "Просунута виробнича будівля."),
            ("Improved", "Покращена"),
            ("Used to extract", "Використовується для видобутку"),
            ("Can be placed only on", "Можна розмістити лише на"),
            ("deposits", "родовищах"),
            ("Refines raw", "Переробляє сирий"),
            ("into Products", "у продукти"),
            ("Utilizes", "Використовує"),
            ("Utilises", "Використовує"),
            ("heat", "нагрів"),
            ("chemical reactions", "хімічні реакції"),
            ("high speed machining", "високошвидкісну обробку"),
            ("nanoscale printing", "нанодрук"),
            ("to make Products", "для виробництва продуктів"),
            ("Offers optimized recipes designed for higher production output.", "Пропонує оптимізовані рецепти для вищої продуктивності."),
        ],
    }[language]
    for source, target in replacements:
        translated = translated.replace(source, target)
    return translated, translated != value


def row_dicts(conn: sqlite3.Connection, sql: str) -> list[dict[str, Any]]:
    return [dict(row) for row in conn.execute(sql)]


def build_pack(language: str, conn: sqlite3.Connection) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    review: list[dict[str, Any]] = []
    pack: dict[str, Any] = {
        "language": language,
        "name": LANGUAGE_NAMES[language],
        "items": {},
        "buildings": {},
        "corporations": {},
        "transport_tiers": {},
        "ui": {key: values[language] for key, values in UI.items()},
    }

    for item in row_dicts(conn, "SELECT item_id, name, description FROM items ORDER BY item_id"):
        name, generated_name = translate_name(item["name"], language)
        desc, generated_desc = translate_description(item["description"], language)
        if language != "en" and (generated_name or generated_desc):
            review.append({"section": "items", "id": item["item_id"], "source": item["name"], "language": language})
        pack["items"][item["item_id"]] = {"name": name, "description": desc}

    for building in row_dicts(conn, "SELECT building_id, name, family_name, description FROM buildings ORDER BY building_id"):
        if language == "en":
            name = building["name"]
            family = building["family_name"]
            generated = False
        else:
            name = BUILDINGS[language].get(building["building_id"], translate_name(building["name"], language)[0])
            family = name.removesuffix(" v.2")
            generated = True
        desc, generated_desc = translate_description(building["description"], language)
        if language != "en" and (generated or generated_desc):
            review.append({"section": "buildings", "id": building["building_id"], "source": building["name"], "language": language})
        pack["buildings"][building["building_id"]] = {
            "name": name,
            "family_name": family,
            "description": desc,
        }

    for corporation in row_dicts(conn, "SELECT corporation_id, name, description FROM corporations ORDER BY corporation_id"):
        name = corporation["name"] if language == "en" else CORPORATIONS[language].get(corporation["corporation_id"], corporation["name"])
        desc, generated_desc = translate_description(corporation["description"], language)
        if language != "en":
            review.append({"section": "corporations", "id": corporation["corporation_id"], "source": corporation["name"], "language": language})
        pack["corporations"][corporation["corporation_id"]] = {"name": name, "description": desc}

    rail_names = {
        "en": ["Rail tier 1", "Rail tier 2", "Rail tier 3"],
        "ru": ["Рельсы, уровень 1", "Рельсы, уровень 2", "Рельсы, уровень 3"],
        "de": ["Schienenstufe 1", "Schienenstufe 2", "Schienenstufe 3"],
        "uk": ["Рейки, рівень 1", "Рейки, рівень 2", "Рейки, рівень 3"],
    }[language]
    for index in range(1, 4):
        pack["transport_tiers"][f"rail-tier-{index}"] = {"name": rail_names[index - 1]}
    return pack, review


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    pending: list[dict[str, Any]] = []
    try:
        for language in LANGUAGE_NAMES:
            pack, review = build_pack(language, conn)
            pending.extend(review)
            (OUT_DIR / f"{language}.json").write_text(
                json.dumps(pack, ensure_ascii=False, indent=2) + "\n",
                encoding="utf-8",
            )
    finally:
        conn.close()
    (OUT_DIR / "pending_review.json").write_text(
        json.dumps(
            {
                "note": "Generated translations should be reviewed against official client strings when available.",
                "entries": pending,
            },
            ensure_ascii=False,
            indent=2,
        ) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
